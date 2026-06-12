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
/// (connection, each metric visualization) is an <see cref="ExpandGroup"/>
/// holding a <see cref="ControlSheet"/> bound to a settings object. First
/// visualization: the spatial heatmap (volumetric fog or voxel cubes).
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

	sealed class HeatmapRenderSettings
	{
		[Title( "Show Heatmap" )] public bool Visible { get; set; }
		public HeatmapRenderMode Mode { get; set; } = HeatmapRenderMode.Fog;
		[Range( 0.1f, 20f )] public float Density { get; set; } = 3.5f;
		[Range( 0.25f, 5f )] public float Falloff { get; set; } = 1.8f;
		[Range( 16f, 256f )] public float Steps { get; set; } = 96f;
	}

	readonly ConnectionSettings _connection = new();
	readonly HeatmapQuerySettings _query = new();
	readonly HeatmapRenderSettings _render = new();

	readonly ComboBox _scene;
	Label _connectionStatus;
	Label _heatmapStatus;
	ExpandGroup _connectionGroup;
	ExpandGroup _heatmapGroup;

	readonly HeatmapOverlay _overlay = new();

	// Last fetched dataset: look-only changes (sliders, render mode) rebuild
	// from this without another network round-trip.
	List<HeatmapVoxel> _voxels;
	DensityGrid _grid;
	float _fetchedVoxelSize;
	bool _fetchedUseMetric;
	bool _fetching;

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

		// --- Heatmap visualization -------------------------------------------
		_scene = new ComboBox( this );
		canvas.Add( BuildHeatmapGroup() );
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

	void SetHeatmapStatus( string text ) => SetStatus( _heatmapStatus, _heatmapGroup, text );

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

	Widget BuildHeatmapGroup()
	{
		var content = new Widget( this );
		content.Layout = Layout.Column();
		content.Layout.Margin = new Sandbox.UI.Margin( 8f, 8f, 8f, 8f );
		content.Layout.Spacing = 4f;

		var sheet = new ControlSheet();

		var renderSo = EditorUtility.GetSerializedObject( _render );
		renderSo.OnPropertyChanged += OnRenderSettingChanged;
		sheet.AddObject( renderSo );

		// Scene comes from the API, not free text — a combo fed by Load Scenes.
		// Label width matches the sheet's label column (min 120, spacing 10) so
		// the combo lines up with the other controls.
		var sceneRow = Layout.Row();
		sceneRow.Spacing = 10f;
		var sceneLabel = new Label( "Scene", this );
		sceneLabel.FixedWidth = 120f;
		sceneRow.Add( sceneLabel );
		sceneRow.Add( _scene, 1 );
		var loadScenes = new Button( "", "refresh", this );
		loadScenes.ToolTip = "Load scenes with spatial data";
		loadScenes.Clicked = () => _ = LoadScenesAsync();
		sceneRow.Add( loadScenes );
		sheet.AddLayout( sceneRow );

		var querySo = EditorUtility.GetSerializedObject( _query );
		sheet.AddObject( querySo );

		content.Layout.Add( sheet );

		var refresh = new Button( "Refresh", "refresh", this );
		refresh.Clicked = () => _ = RefreshAsync();
		content.Layout.Add( refresh );

		_heatmapStatus = MakeStatusLabel( content );

		_heatmapGroup = MakeGroup( "Heatmap", "local_fire_department", "heatmap", content );
		return _heatmapGroup;
	}

	void OnRenderSettingChanged( SerializedProperty prop )
	{
		switch ( prop.Name )
		{
			case nameof( HeatmapRenderSettings.Visible ):
			case nameof( HeatmapRenderSettings.Mode ):
				if ( _render.Visible && _grid != null )
					RebuildOverlay();
				else
					_overlay.Hide();
				break;
			default:
				// Fog look params live in material attributes — update in place,
				// no re-fetch. (Steps/density/falloff don't affect the cubes mesh.)
				_overlay.UpdateLook( _render.Density, _render.Falloff, _render.Steps );
				break;
		}
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

	async System.Threading.Tasks.Task LoadScenesAsync()
	{
		try
		{
			SetHeatmapStatus( "Loading scenes…" );
			var response = await CreateClient().GetScenesAsync( _query.From.Trim(), _query.To.Trim() );
			var selected = _scene.CurrentText;
			_scene.Clear();
			foreach ( var scene in response.Scenes )
				_scene.AddItem( scene.Scene );
			if ( !string.IsNullOrEmpty( selected ) && response.Scenes.Any( s => s.Scene == selected ) )
				_scene.TrySelectNamed( selected );
			SetHeatmapStatus( response.Scenes.Count == 0 ? "No scenes with spatial data in this range." : $"{response.Scenes.Count} scene(s)." );
		}
		catch ( SpatialApiException e )
		{
			SetHeatmapStatus( e.StatusCode == 401 ? "Invalid API key." : $"Scene fetch failed: {e.Message}" );
		}
	}

	async System.Threading.Tasks.Task RefreshAsync()
	{
		if ( _fetching )
			return;

		var scene = _scene.CurrentText;
		if ( string.IsNullOrWhiteSpace( scene ) )
		{
			SetHeatmapStatus( "Pick a scene first (Load Scenes)." );
			return;
		}
		if ( _query.VoxelSize <= 0f )
		{
			SetHeatmapStatus( "Voxel size must be a positive number." );
			return;
		}

		var metricKey = _query.MetricKey.Trim();
		var useMetric = !string.IsNullOrEmpty( metricKey );

		try
		{
			_fetching = true;
			SetHeatmapStatus( "Fetching voxels…" );
			var response = await CreateClient().GetVoxelsAsync( new SpatialApiClient.VoxelsQuery
			{
				Scene = scene,
				VoxelSize = _query.VoxelSize,
				From = _query.From.Trim(),
				To = _query.To.Trim(),
				EventType = _query.EventType.Trim(),
				MetricKey = useMetric ? metricKey : null,
				MetricAgg = _query.MetricAgg.ToString().ToLowerInvariant(),
			} );

			_voxels = response.Voxels
				.Select( v => new HeatmapVoxel( v.X, v.Y, v.Z, v.Count, v.Value ) )
				.ToList();
			_fetchedVoxelSize = response.VoxelSize;
			_fetchedUseMetric = useMetric;
			_grid = DensityGridBuilder.Build( _voxels, _fetchedVoxelSize, useMetric );

			if ( _voxels.Count == 0 )
			{
				_overlay.Hide();
				SetHeatmapStatus( "No data for this query." );
				return;
			}
			if ( _grid == null )
			{
				_overlay.Hide();
				SetHeatmapStatus( $"Grid exceeds {DensityGridBuilder.MaxGridCells:N0} cells — try a larger voxel size." );
				return;
			}

			_render.Visible = true;
			RebuildOverlay();
			SetHeatmapStatus( response.Truncated
				? $"{_voxels.Count} voxels (result limit hit — data truncated)."
				: $"{_voxels.Count} voxels." );
		}
		catch ( SpatialApiException e )
		{
			SetHeatmapStatus( e.StatusCode switch
			{
				401 => "Invalid API key.",
				400 => "Invalid query parameters.",
				0 => $"Network error: {e.Message}. Check the ingest URL and retry.",
				_ => $"Request failed: {e.Message}",
			} );
		}
		finally
		{
			_fetching = false;
		}
	}

	void RebuildOverlay()
	{
		if ( !_render.Visible || _grid == null )
			return;

		var world = SceneEditorSession.Active?.Scene?.SceneWorld;
		if ( world == null )
		{
			SetHeatmapStatus( "No active editor scene." );
			return;
		}

		_overlay.Show( world, _render.Mode, _grid, _voxels, _fetchedVoxelSize, _fetchedUseMetric,
			_render.Density, _render.Falloff, _render.Steps );
	}

	public override void OnDestroyed()
	{
		_overlay.Dispose();
		base.OnDestroyed();
	}
}
