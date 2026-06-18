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

	// Voxel size is no longer a dock control (its down-sampling needs fixing). A
	// fixed cell size still satisfies the ingest query and the density grid build.
	const float DefaultVoxelSize = 64f;

	sealed class ConnectionSettings
	{
		[Title( "Ingest URL" )] public string IngestUrl { get; set; } = DefaultIngestUrl;
		[Title( "Secret Key" )] public string SecretKey { get; set; } = "";
	}

	// Shared query window for every visualizer section. Event type is picked from
	// a fetched combo (see VisualizerSection), not stored here.
	sealed class DateRangeSettings
	{
		[Title( "From (yyyy-mm-dd)" )] public string From { get; set; } = DateTime.UtcNow.AddDays( -30 ).ToString( DateFormat );
		[Title( "To (yyyy-mm-dd)" )] public string To { get; set; } = DateTime.UtcNow.ToString( DateFormat );
	}

	sealed class FogRenderSettings
	{
		[Title( "Show Fog" )] public bool Visible { get; set; }
		[Range( 0.1f, 20f )] public float Density { get; set; } = 3.5f;
		[Range( 0.25f, 5f )] public float Falloff { get; set; } = 1.8f;
	}

	sealed class CubesRenderSettings
	{
		[Title( "Show Cubes" )] public bool Visible { get; set; }
	}

	sealed class LinesRenderSettings
	{
		[Title( "Show Path Lines" )] public bool Visible { get; set; }
		[Title( "Line Width" ), Range( 0.5f, 16f )] public float LineWidth { get; set; } = 3f;
	}

	/// <summary>
	/// Shared UI handles every visualizer section owns: its scene picker, status
	/// label, group, render-settings object, query window, and in-flight guard.
	/// </summary>
	abstract class SectionBase
	{
		public readonly DateRangeSettings Query = new();
		public ComboBox Scene;
		public Label Status;
		public ExpandGroup Group;
		public SerializedObject RenderSo;
		public bool Fetching;
	}

	/// <summary>Voxel heatmap section (fog or cubes): adds an event-type picker and the fetched grid.</summary>
	sealed class VisualizerSection : SectionBase
	{
		public ComboBox EventType;

		// Look-only changes rebuild from this without another network round-trip.
		public List<HeatmapVoxel> Voxels;
		public DensityGrid Grid;
		public float FetchedVoxelSize;
	}

	/// <summary>Path Lines section: holds the fetched per-player trajectories.</summary>
	sealed class TrajectorySection : SectionBase
	{
		public List<TrajectoryDto> Trajectories;
	}

	readonly ConnectionSettings _connection = new();
	readonly FogRenderSettings _fog = new();
	readonly CubesRenderSettings _cubes = new();
	readonly LinesRenderSettings _lines = new();
	readonly VisualizerSection _fogSection = new();
	readonly VisualizerSection _cubesSection = new();
	readonly TrajectorySection _linesSection = new();

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
		_connection.SecretKey = ProjectCookie.Get( $"{CookiePrefix}.secretkey",
			ProjectCookie.Get( $"{CookiePrefix}.apikey",
				ProjectCookie.Get( $"{LegacyCookiePrefix}.apikey", "" ) ) );

		canvas.Add( BuildConnectionGroup() );

		// --- Visualizers ------------------------------------------------------
		canvas.Add( BuildVisualizerGroup( _fogSection, _fog, OnFogSettingChanged,
			"Fog Heatmap", "cloud_queue", "fog" ) );
		canvas.Add( BuildVisualizerGroup( _cubesSection, _cubes, OnCubesSettingChanged,
			"Voxel Heatmap", "view_in_ar", "cubes" ) );
		canvas.Add( BuildTrajectoryGroup( _linesSection, _lines, OnLinesSettingChanged,
			"Path Lines", "timeline", "lines" ) );
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

	static void SetSectionStatus( SectionBase section, string text ) => SetStatus( section.Status, section.Group, text );

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
			ProjectCookie.Set( $"{CookiePrefix}.secretkey", _connection.SecretKey );
		};
		// The key needs the masked control — the default string control echoes
		// plain text — so it's added as its own row, not via AddObject.
		sheet.AddObject( so, p => p.Name != nameof( ConnectionSettings.SecretKey ) );
		sheet.AddControl<SecretStringControlWidget>( so.GetProperty( nameof( ConnectionSettings.SecretKey ) ) );

		content.Layout.Add( sheet );

		var testConnection = new Button( "Test Connection", "network_check", this );
		testConnection.Clicked = () => _ = TestConnectionAsync();
		content.Layout.Add( testConnection );

		_connectionStatus = MakeStatusLabel( content );

		_connectionGroup = MakeGroup( "Connection", "cloud", "connection", content );
		return _connectionGroup;
	}

	// A combo fed from the API (not free text) with its own refresh button, laid
	// into the sheet's grid. Label width matches the sheet's label column (min
	// 120, spacing 10) so the combo lines up with the other controls.
	ComboBox AddComboRow( ControlSheet sheet, string label, System.Action onRefresh, string refreshTip )
	{
		var combo = new ComboBox( this );
		var row = Layout.Row();
		row.Spacing = 10f;
		var lbl = new Label( label, this );
		lbl.FixedWidth = 120f;
		row.Add( lbl );
		row.Add( combo, 1 );
		var refresh = new Button( "", "refresh", this );
		refresh.ToolTip = refreshTip;
		refresh.Clicked = onRefresh;
		row.Add( refresh );
		sheet.AddLayout( row );
		return combo;
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

		section.Scene = AddComboRow( sheet, "Scene",
			() => _ = LoadScenesAsync( section ), "Load scenes with spatial data" );
		section.EventType = AddComboRow( sheet, "Event Type",
			() => _ = LoadEventTypesAsync( section ), "Load spatial event types in range" );

		sheet.AddObject( EditorUtility.GetSerializedObject( section.Query ) );

		content.Layout.Add( sheet );

		var refresh = new Button( "Refresh", "refresh", this );
		refresh.Clicked = () => _ = RefreshAsync( section );
		content.Layout.Add( refresh );

		section.Status = MakeStatusLabel( content );

		section.Group = MakeGroup( title, icon, cookie, content );
		return section.Group;
	}

	Widget BuildTrajectoryGroup( TrajectorySection section, object renderSettings,
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

		section.Scene = AddComboRow( sheet, "Scene",
			() => _ = LoadScenesAsync( section ), "Load scenes with spatial data" );

		sheet.AddObject( EditorUtility.GetSerializedObject( section.Query ) );

		content.Layout.Add( sheet );

		var refresh = new Button( "Refresh", "refresh", this );
		refresh.Clicked = () => _ = RefreshTrajectoriesAsync( section );
		content.Layout.Add( refresh );

		section.Status = MakeStatusLabel( content );

		section.Group = MakeGroup( title, icon, cookie, content );
		return section.Group;
	}

	void OnFogSettingChanged( SerializedProperty prop )
	{
		if ( prop.Name == nameof( FogRenderSettings.Visible ) )
		{
			// The overlay holds one scene object — the visualizers are exclusive.
			if ( _fog.Visible )
				DisableOthers( _fogSection.RenderSo );
			RebuildOverlay();
			return;
		}

		// Fog look params live in material attributes — update in place, no re-fetch.
		_overlay.UpdateLook( _fog.Density, _fog.Falloff );
	}

	void OnCubesSettingChanged( SerializedProperty prop )
	{
		if ( prop.Name != nameof( CubesRenderSettings.Visible ) )
			return;

		if ( _cubes.Visible )
			DisableOthers( _cubesSection.RenderSo );
		RebuildOverlay();
	}

	void OnLinesSettingChanged( SerializedProperty prop )
	{
		if ( prop.Name == nameof( LinesRenderSettings.Visible ) )
		{
			if ( _lines.Visible )
				DisableOthers( _linesSection.RenderSo );
			RebuildOverlay();
			return;
		}

		// Line width is baked per-vertex, so a width change rebuilds the line
		// object from the cached trajectories — no re-fetch.
		if ( _lines.Visible )
			RebuildOverlay();
	}

	// Keep the three visualizers mutually exclusive: enabling one disables the
	// others so only a single object ever lives in the shared overlay.
	void DisableOthers( SerializedObject active )
	{
		foreach ( var so in new[] { _fogSection.RenderSo, _cubesSection.RenderSo, _linesSection.RenderSo } )
		{
			if ( so != null && so != active )
				SetVisibleToggle( so, false );
		}
	}

	static void SetVisibleToggle( SerializedObject so, bool value )
	{
		// Through the SerializedObject so the checkbox widget updates too. Guard:
		// only flip when needed, else the change events ping-pong.
		var prop = so?.GetProperty( "Visible" );
		if ( prop != null && prop.GetValue( false ) != value )
			prop.SetValue( value );
	}

	VisualizerSection ActiveVoxelSection =>
		_fog.Visible ? _fogSection :
		_cubes.Visible ? _cubesSection : null;

	SpatialApiClient CreateClient() => new( _connection.IngestUrl, _connection.SecretKey );

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
				401 => "Invalid secret key.",
				0 => $"Network error: {e.Message}. Check the ingest URL and retry.",
				_ => $"Connection failed: {e.Message}",
			} );
		}
	}

	async System.Threading.Tasks.Task LoadScenesAsync( SectionBase section )
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
			SetSectionStatus( section, e.StatusCode == 401 ? "Invalid secret key." : $"Scene fetch failed: {e.Message}" );
		}
	}

	// Event types are scoped to the picked scene when one is selected, else to the
	// whole range. The list is spatial-only (server-side), so every option yields
	// a non-empty heatmap.
	async System.Threading.Tasks.Task LoadEventTypesAsync( VisualizerSection section )
	{
		try
		{
			SetSectionStatus( section, "Loading event types…" );
			var scene = section.Scene.CurrentText;
			var response = await CreateClient().GetEventTypesAsync(
				section.Query.From.Trim(), section.Query.To.Trim(),
				string.IsNullOrWhiteSpace( scene ) ? null : scene );
			var selected = section.EventType.CurrentText;
			section.EventType.Clear();
			foreach ( var type in response.EventTypes )
				section.EventType.AddItem( type );
			if ( !string.IsNullOrEmpty( selected ) && response.EventTypes.Any( t => t == selected ) )
				section.EventType.TrySelectNamed( selected );
			SetSectionStatus( section, response.EventTypes.Count == 0
				? "No spatial event types in this range."
				: $"{response.EventTypes.Count} event type(s)." );
		}
		catch ( SpatialApiException e )
		{
			SetSectionStatus( section, e.StatusCode == 401 ? "Invalid secret key." : $"Event-type fetch failed: {e.Message}" );
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

		// Empty event type aggregates every spatial type for the scene.
		var eventType = section.EventType.CurrentText;

		try
		{
			section.Fetching = true;
			SetSectionStatus( section, "Fetching voxels…" );
			var response = await CreateClient().GetVoxelsAsync( new SpatialApiClient.VoxelsQuery
			{
				Scene = scene,
				VoxelSize = DefaultVoxelSize,
				From = section.Query.From.Trim(),
				To = section.Query.To.Trim(),
				EventType = string.IsNullOrWhiteSpace( eventType ) ? "" : eventType.Trim(),
			} );

			section.Voxels = response.Voxels
				.Select( v => new HeatmapVoxel( v.X, v.Y, v.Z, v.Count, v.Value ) )
				.ToList();
			section.FetchedVoxelSize = response.VoxelSize;
			section.Grid = DensityGridBuilder.Build( section.Voxels, section.FetchedVoxelSize, false );

			if ( section.Voxels.Count == 0 )
			{
				if ( ActiveVoxelSection == section )
					_overlay.Hide();
				SetSectionStatus( section, "No data for this query." );
				return;
			}
			if ( section.Grid == null )
			{
				if ( ActiveVoxelSection == section )
					_overlay.Hide();
				SetSectionStatus( section, $"Grid exceeds {DensityGridBuilder.MaxGridCells:N0} cells for this scene." );
				return;
			}

			// Refreshing a section makes it the shown one; the toggle change
			// handler turns the others off and rebuilds the overlay.
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
				401 => "Invalid secret key.",
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

	async System.Threading.Tasks.Task RefreshTrajectoriesAsync( TrajectorySection section )
	{
		if ( section.Fetching )
			return;

		var scene = section.Scene.CurrentText;
		if ( string.IsNullOrWhiteSpace( scene ) )
		{
			SetSectionStatus( section, "Pick a scene first (Load Scenes)." );
			return;
		}

		try
		{
			section.Fetching = true;
			SetSectionStatus( section, "Fetching trajectories…" );
			var response = await CreateClient().GetTrajectoriesAsync(
				scene, section.Query.From.Trim(), section.Query.To.Trim() );

			section.Trajectories = response.Trajectories;

			if ( section.Trajectories.Count == 0 )
			{
				if ( _lines.Visible )
					_overlay.Hide();
				SetSectionStatus( section, "No trajectories for this query." );
				return;
			}

			SetVisibleToggle( section.RenderSo, true );
			RebuildOverlay();
			SetSectionStatus( section, response.Truncated
				? $"{section.Trajectories.Count} paths (result limit hit — data truncated)."
				: $"{section.Trajectories.Count} paths." );
		}
		catch ( SpatialApiException e )
		{
			SetSectionStatus( section, e.StatusCode switch
			{
				401 => "Invalid secret key.",
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
		var world = SceneEditorSession.Active?.Scene?.SceneWorld;

		if ( _fog.Visible && _fogSection.Grid != null )
		{
			if ( world == null ) { SetSectionStatus( _fogSection, "No active editor scene." ); return; }
			_overlay.Show( world, HeatmapRenderMode.Fog, _fogSection.Grid, _fogSection.Voxels,
				_fogSection.FetchedVoxelSize, false, _fog.Density, _fog.Falloff );
		}
		else if ( _cubes.Visible && _cubesSection.Grid != null )
		{
			if ( world == null ) { SetSectionStatus( _cubesSection, "No active editor scene." ); return; }
			_overlay.Show( world, HeatmapRenderMode.Cubes, _cubesSection.Grid, _cubesSection.Voxels,
				_cubesSection.FetchedVoxelSize, false, _fog.Density, _fog.Falloff );
		}
		else if ( _lines.Visible && _linesSection.Trajectories is { Count: > 0 } )
		{
			if ( world == null ) { SetSectionStatus( _linesSection, "No active editor scene." ); return; }
			_overlay.ShowLines( world, _linesSection.Trajectories, _lines.LineWidth );
		}
		else
		{
			_overlay.Hide();
		}
	}

	public override void OnDestroyed()
	{
		_overlay.Dispose();
		base.OnDestroyed();
	}
}
