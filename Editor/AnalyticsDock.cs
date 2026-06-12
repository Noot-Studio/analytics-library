using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace Noot.Analytics.Editor;

/// <summary>
/// ControlSheet row for secret strings: wraps <see cref="SecretLineEdit"/> so
/// the key sits in the sheet's grid (aligned with other rows) but stays masked.
/// </summary>
sealed class SecretStringControlWidget : ControlWidget
{
	readonly SecretLineEdit _edit;

	public SecretStringControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		_edit = new SecretLineEdit( this )
		{
			Secret = property.GetValue( "" ),
		};
		_edit.SecretEdited = () => property.SetValue( _edit.Secret );
		Layout.Add( _edit );
	}
}

/// <summary>
/// Password-style line edit: s&amp;box's LineEdit has no echo mode, so the real
/// value is held aside and the widget shows bullets whenever it isn't focused.
/// </summary>
sealed class SecretLineEdit : LineEdit
{
	string _secret = "";
	bool _editing;

	public Action SecretEdited { get; set; }

	public string Secret
	{
		get => _editing ? Text : _secret;
		set
		{
			_secret = value ?? "";
			if ( !_editing )
				Text = Mask( _secret );
		}
	}

	public SecretLineEdit( Widget parent ) : base( parent ) { }

	static string Mask( string value ) => new( '•', Math.Min( value.Length, 24 ) );

	protected override void OnFocus( FocusChangeReason reason )
	{
		base.OnFocus( reason );
		_editing = true;
		Text = _secret;
		SelectAll();
	}

	protected override void OnBlur( FocusChangeReason reason )
	{
		if ( _editing )
		{
			_secret = Text;
			Text = Mask( _secret );
			_editing = false;
			SecretEdited?.Invoke();
		}
		base.OnBlur( reason );
	}
}

/// <summary>
/// Dockable "Analytics" editor window, styled like the inspector: each concern
/// (connection, each visualizer) is an <see cref="ExpandGroup"/> holding a
/// <see cref="ControlSheet"/> bound to a settings object. Each visualizer
/// section is self-contained — its own query settings, fetched dataset, and
/// status — and the two share one scene overlay, so showing one hides the other.
/// Editor-only: the key is stored in a per-project cookie in user-local editor
/// data, never in scene files or builds.
/// </summary>
[Dock( "Editor", "Analytics", "insights" )]
public sealed class AnalyticsDock : Widget
{
	const string CookiePrefix = "analytics";
	// Pre-rename cookie prefix, read once as a fallback so stored keys survive.
	const string LegacyCookiePrefix = "analytics.heatmap";
	const string DefaultIngestUrl = "https://ingest.sbox-analytics.com";
	const string DateFormat = "yyyy-MM-dd";

	public enum MetricAggregation
	{
		Avg,
		Max,
		Min,
		Sum,
	}

	sealed class ConnectionSettings
	{
		[Title( "Ingest URL" )] public string IngestUrl { get; set; } = DefaultIngestUrl;
		[Title( "Secret API Key" )] public string ApiKey { get; set; } = "";
	}

	sealed class HeatmapQuerySettings
	{
		[Title( "Event Type" )] public string EventType { get; set; } = "";
		[Title( "From (yyyy-mm-dd)" )] public string From { get; set; } = DateTime.UtcNow.AddDays( -30 ).ToString( DateFormat );
		[Title( "To (yyyy-mm-dd)" )] public string To { get; set; } = DateTime.UtcNow.ToString( DateFormat );
		[Title( "Voxel Size" )] public float VoxelSize { get; set; } = 64f;
		[Title( "Metric Key" )] public string MetricKey { get; set; } = "";
		[Title( "Metric Aggregation" )] public MetricAggregation MetricAgg { get; set; } = MetricAggregation.Avg;
	}

	sealed class FogRenderSettings
	{
		[Title( "Show Fog" )] public bool Visible { get; set; }
		[Range( 0.1f, 20f )] public float Density { get; set; } = 3.5f;
		[Range( 0.25f, 5f )] public float Falloff { get; set; } = 1.8f;
		[Range( 16f, 256f )] public float Steps { get; set; } = 96f;
	}

	sealed class CubesRenderSettings
	{
		[Title( "Show Cubes" )] public bool Visible { get; set; }
	}

	/// <summary>
	/// One visualizer's UI handles and last-fetched dataset. Each section owns
	/// its query settings so the two visualizers can target different data.
	/// </summary>
	sealed class VisualizerSection
	{
		public readonly HeatmapQuerySettings Query = new();
		public ComboBox Scene;
		public Label Status;
		public ExpandGroup Group;
		public SerializedObject RenderSo;

		// Look-only changes rebuild from this without another network round-trip.
		public List<HeatmapVoxel> Voxels;
		public DensityGrid Grid;
		public float FetchedVoxelSize;
		public bool FetchedUseMetric;
		public bool Fetching;
	}

	readonly ConnectionSettings _connection = new();
	readonly FogRenderSettings _fog = new();
	readonly CubesRenderSettings _cubes = new();
	readonly VisualizerSection _fogSection = new();
	readonly VisualizerSection _cubesSection = new();

	Label _connectionStatus;
	ExpandGroup _connectionGroup;

	readonly HeatmapOverlay _overlay = new();

	public AnalyticsDock( Widget parent ) : base( parent )
	{
		MinimumWidth = 280f;
		Layout = Layout.Column();

		var scroll = new ScrollArea( this );
		scroll.Canvas = new Widget( scroll );
		scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		scroll.Canvas.Layout = Layout.Column();
		scroll.Canvas.Layout.Margin = 4f;
		scroll.Canvas.Layout.Spacing = 4f;
		Layout.Add( scroll );

		var canvas = scroll.Canvas.Layout;

		// --- Connection -----------------------------------------------------
		_connection.IngestUrl = ProjectCookie.Get( $"{CookiePrefix}.ingesturl",
			ProjectCookie.Get( $"{LegacyCookiePrefix}.ingesturl", DefaultIngestUrl ) );
		_connection.ApiKey = ProjectCookie.Get( $"{CookiePrefix}.apikey",
			ProjectCookie.Get( $"{LegacyCookiePrefix}.apikey", "" ) );

		canvas.Add( BuildConnectionGroup() );

		// --- Visualizers ------------------------------------------------------
		canvas.Add( BuildVisualizerGroup( _fogSection, _fog, OnFogSettingChanged,
			"Fog Heatmap", "cloud_queue", "fog" ) );
		canvas.Add( BuildVisualizerGroup( _cubesSection, _cubes, OnCubesSettingChanged,
			"Voxel Heatmap", "view_in_ar", "cubes" ) );
		canvas.AddStretchCell();
	}

	// Status labels live inside their section so feedback shows next to the
	// action that produced it. Text changes alter content height — tell the
	// group to re-measure.
	Label MakeStatusLabel( Widget content )
	{
		var label = new Label( "", content );
		label.WordWrap = true;
		content.Layout.Add( label );
		return label;
	}

	static void SetStatus( Label label, ExpandGroup group, string text )
	{
		label.Text = text;
		// A word-wrapping Label's size hint is its single unwrapped line, so the
		// ExpandGroup reserves one row and the overflowing text squeezes the rows
		// above into each other. Measure the wrapped height and pin it instead.
		if ( string.IsNullOrEmpty( text ) )
		{
			label.FixedHeight = 0f;
		}
		else
		{
			var width = label.Width > 50f ? label.Width : 200f;
			var measured = Paint.MeasureText( new Rect( 0f, 0f, width, 4096f ), text, TextFlag.LeftTop | TextFlag.WordWrap );
			label.FixedHeight = Math.Max( measured.Height + 4f, Theme.RowHeight );
		}
		label.Parent?.AdjustSize();
		group.SetHeight();
	}

	void SetConnectionStatus( string text ) => SetStatus( _connectionStatus, _connectionGroup, text );

	static void SetSectionStatus( VisualizerSection section, string text ) => SetStatus( section.Status, section.Group, text );

	ExpandGroup MakeGroup( string title, string icon, string cookie, Widget content )
	{
		var group = new ExpandGroup( this )
		{
			Title = title,
			Icon = icon,
		};
		group.SetWidget( content );
		group.SetOpenState( true );
		group.StateCookieName = $"{CookiePrefix}.section.{cookie}";
		return group;
	}

	Widget BuildConnectionGroup()
	{
		var content = new Widget( this );
		content.Layout = Layout.Column();
		content.Layout.Margin = new Sandbox.UI.Margin( 8f, 8f, 8f, 8f );
		content.Layout.Spacing = 4f;

		var sheet = new ControlSheet();
		var so = EditorUtility.GetSerializedObject( _connection );
		so.OnPropertyChanged += _ =>
		{
			ProjectCookie.Set( $"{CookiePrefix}.ingesturl", _connection.IngestUrl );
			ProjectCookie.Set( $"{CookiePrefix}.apikey", _connection.ApiKey );
		};
		// The key needs the masked control — the default string control echoes
		// plain text — so it's added as its own row, not via AddObject.
		sheet.AddObject( so, p => p.Name != nameof( ConnectionSettings.ApiKey ) );
		sheet.AddControl<SecretStringControlWidget>( so.GetProperty( nameof( ConnectionSettings.ApiKey ) ) );

		content.Layout.Add( sheet );

		var testConnection = new Button( "Test Connection", "network_check", this );
		testConnection.Clicked = () => _ = TestConnectionAsync();
		content.Layout.Add( testConnection );

		_connectionStatus = MakeStatusLabel( content );

		_connectionGroup = MakeGroup( "Connection", "cloud", "connection", content );
		return _connectionGroup;
	}

	Widget BuildVisualizerGroup( VisualizerSection section, object renderSettings,
		SerializedObject.PropertyChangedDelegate onRenderChanged, string title, string icon, string cookie )
	{
		var content = new Widget( this );
		content.Layout = Layout.Column();
		content.Layout.Margin = new Sandbox.UI.Margin( 8f, 8f, 8f, 8f );
		content.Layout.Spacing = 4f;

		var sheet = new ControlSheet();

		section.RenderSo = EditorUtility.GetSerializedObject( renderSettings );
		section.RenderSo.OnPropertyChanged += onRenderChanged;
		sheet.AddObject( section.RenderSo );

		// Scene comes from the API, not free text — a combo fed by Load Scenes.
		// Label width matches the sheet's label column (min 120, spacing 10) so
		// the combo lines up with the other controls.
		section.Scene = new ComboBox( this );
		var sceneRow = Layout.Row();
		sceneRow.Spacing = 10f;
		var sceneLabel = new Label( "Scene", this );
		sceneLabel.FixedWidth = 120f;
		sceneRow.Add( sceneLabel );
		sceneRow.Add( section.Scene, 1 );
		var loadScenes = new Button( "", "refresh", this );
		loadScenes.ToolTip = "Load scenes with spatial data";
		loadScenes.Clicked = () => _ = LoadScenesAsync( section );
		sceneRow.Add( loadScenes );
		sheet.AddLayout( sceneRow );

		sheet.AddObject( EditorUtility.GetSerializedObject( section.Query ) );

		content.Layout.Add( sheet );

		var refresh = new Button( "Refresh", "refresh", this );
		refresh.Clicked = () => _ = RefreshAsync( section );
		content.Layout.Add( refresh );

		section.Status = MakeStatusLabel( content );

		section.Group = MakeGroup( title, icon, cookie, content );
		return section.Group;
	}

	void OnFogSettingChanged( SerializedProperty prop )
	{
		if ( prop.Name == nameof( FogRenderSettings.Visible ) )
		{
			// The overlay holds one scene object — the two modes are exclusive.
			if ( _fog.Visible )
				SetVisibleToggle( _cubesSection.RenderSo, false );
			RebuildOrHide();
			return;
		}

		// Fog look params live in material attributes — update in place, no re-fetch.
		_overlay.UpdateLook( _fog.Density, _fog.Falloff, _fog.Steps );
	}

	void OnCubesSettingChanged( SerializedProperty prop )
	{
		if ( prop.Name != nameof( CubesRenderSettings.Visible ) )
			return;

		if ( _cubes.Visible )
			SetVisibleToggle( _fogSection.RenderSo, false );
		RebuildOrHide();
	}

	static void SetVisibleToggle( SerializedObject so, bool value )
	{
		// Through the SerializedObject so the checkbox widget updates too. Guard:
		// only flip when needed, else the change events ping-pong.
		var prop = so?.GetProperty( "Visible" );
		if ( prop != null && prop.GetValue( false ) != value )
			prop.SetValue( value );
	}

	VisualizerSection ActiveSection =>
		_fog.Visible ? _fogSection :
		_cubes.Visible ? _cubesSection : null;

	void RebuildOrHide()
	{
		if ( ActiveSection?.Grid != null )
			RebuildOverlay();
		else
			_overlay.Hide();
	}

	SpatialApiClient CreateClient() => new( _connection.IngestUrl, _connection.ApiKey );

	async System.Threading.Tasks.Task TestConnectionAsync()
	{
		try
		{
			SetConnectionStatus( "Testing connection…" );
			var to = DateTime.UtcNow.ToString( DateFormat );
			var from = DateTime.UtcNow.AddDays( -1 ).ToString( DateFormat );
			await CreateClient().GetScenesAsync( from, to );
			SetConnectionStatus( "Connection OK." );
		}
		catch ( SpatialApiException e )
		{
			SetConnectionStatus( e.StatusCode switch
			{
				401 => "Invalid API key.",
				0 => $"Network error: {e.Message}. Check the ingest URL and retry.",
				_ => $"Connection failed: {e.Message}",
			} );
		}
	}

	async System.Threading.Tasks.Task LoadScenesAsync( VisualizerSection section )
	{
		try
		{
			SetSectionStatus( section, "Loading scenes…" );
			var response = await CreateClient().GetScenesAsync( section.Query.From.Trim(), section.Query.To.Trim() );
			var selected = section.Scene.CurrentText;
			section.Scene.Clear();
			foreach ( var scene in response.Scenes )
				section.Scene.AddItem( scene.Scene );
			if ( !string.IsNullOrEmpty( selected ) && response.Scenes.Any( s => s.Scene == selected ) )
				section.Scene.TrySelectNamed( selected );
			SetSectionStatus( section, response.Scenes.Count == 0 ? "No scenes with spatial data in this range." : $"{response.Scenes.Count} scene(s)." );
		}
		catch ( SpatialApiException e )
		{
			SetSectionStatus( section, e.StatusCode == 401 ? "Invalid API key." : $"Scene fetch failed: {e.Message}" );
		}
	}

	async System.Threading.Tasks.Task RefreshAsync( VisualizerSection section )
	{
		if ( section.Fetching )
			return;

		var scene = section.Scene.CurrentText;
		if ( string.IsNullOrWhiteSpace( scene ) )
		{
			SetSectionStatus( section, "Pick a scene first (Load Scenes)." );
			return;
		}
		if ( section.Query.VoxelSize <= 0f )
		{
			SetSectionStatus( section, "Voxel size must be a positive number." );
			return;
		}

		var metricKey = section.Query.MetricKey.Trim();
		var useMetric = !string.IsNullOrEmpty( metricKey );

		try
		{
			section.Fetching = true;
			SetSectionStatus( section, "Fetching voxels…" );
			var response = await CreateClient().GetVoxelsAsync( new SpatialApiClient.VoxelsQuery
			{
				Scene = scene,
				VoxelSize = section.Query.VoxelSize,
				From = section.Query.From.Trim(),
				To = section.Query.To.Trim(),
				EventType = section.Query.EventType.Trim(),
				MetricKey = useMetric ? metricKey : null,
				MetricAgg = section.Query.MetricAgg.ToString().ToLowerInvariant(),
			} );

			section.Voxels = response.Voxels
				.Select( v => new HeatmapVoxel( v.X, v.Y, v.Z, v.Count, v.Value ) )
				.ToList();
			section.FetchedVoxelSize = response.VoxelSize;
			section.FetchedUseMetric = useMetric;
			section.Grid = DensityGridBuilder.Build( section.Voxels, section.FetchedVoxelSize, useMetric );

			if ( section.Voxels.Count == 0 )
			{
				if ( ActiveSection == section )
					_overlay.Hide();
				SetSectionStatus( section, "No data for this query." );
				return;
			}
			if ( section.Grid == null )
			{
				if ( ActiveSection == section )
					_overlay.Hide();
				SetSectionStatus( section, $"Grid exceeds {DensityGridBuilder.MaxGridCells:N0} cells — try a larger voxel size." );
				return;
			}

			// Refreshing a section makes it the shown one; the toggle change
			// handler turns the other section off and rebuilds the overlay.
			SetVisibleToggle( section.RenderSo, true );
			RebuildOverlay();
			SetSectionStatus( section, response.Truncated
				? $"{section.Voxels.Count} voxels (result limit hit — data truncated)."
				: $"{section.Voxels.Count} voxels." );
		}
		catch ( SpatialApiException e )
		{
			SetSectionStatus( section, e.StatusCode switch
			{
				401 => "Invalid API key.",
				400 => "Invalid query parameters.",
				0 => $"Network error: {e.Message}. Check the ingest URL and retry.",
				_ => $"Request failed: {e.Message}",
			} );
		}
		finally
		{
			section.Fetching = false;
		}
	}

	void RebuildOverlay()
	{
		var section = ActiveSection;
		if ( section?.Grid == null )
			return;

		var world = SceneEditorSession.Active?.Scene?.SceneWorld;
		if ( world == null )
		{
			SetSectionStatus( section, "No active editor scene." );
			return;
		}

		var mode = section == _fogSection ? HeatmapRenderMode.Fog : HeatmapRenderMode.Cubes;
		_overlay.Show( world, mode, section.Grid, section.Voxels, section.FetchedVoxelSize, section.FetchedUseMetric,
			_fog.Density, _fog.Falloff, _fog.Steps );
	}

	public override void OnDestroyed()
	{
		_overlay.Dispose();
		base.OnDestroyed();
	}
}
