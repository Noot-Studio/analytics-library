using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

namespace Noot.Analytics.Editor;

/// <summary>
/// Dockable editor window that fetches spatial analytics voxels from the ingest
/// read API (x-api-key auth) and visualizes them in the active editor scene as
/// a volumetric fog heatmap or voxel cubes. Editor-only: the key is stored in a
/// per-project cookie in user-local editor data, never in scene files or builds.
/// </summary>
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

[Dock( "Editor", "Analytics Heatmap", "local_fire_department" )]
public sealed class HeatmapDock : Widget
{
	const string CookiePrefix = "analytics.heatmap";
	const string DefaultIngestUrl = "https://ingest.sbox-analytics.com";
	const string DateFormat = "yyyy-MM-dd";

	readonly SecretLineEdit _apiKey;
	readonly LineEdit _ingestUrl;
	readonly ComboBox _scene;
	readonly LineEdit _eventType;
	readonly LineEdit _fromDate;
	readonly LineEdit _toDate;
	readonly LineEdit _voxelSize;
	readonly LineEdit _metricKey;
	readonly ComboBox _metricAgg;
	readonly ComboBox _renderMode;
	readonly FloatSlider _density;
	readonly FloatSlider _falloff;
	readonly FloatSlider _steps;
	readonly Label _status;

	readonly HeatmapOverlay _overlay = new();

	// Last fetched dataset: look-only changes (sliders, render mode) rebuild
	// from this without another network round-trip.
	List<HeatmapVoxel> _voxels;
	DensityGrid _grid;
	float _fetchedVoxelSize;
	bool _fetchedUseMetric;
	bool _visible;
	bool _fetching;

	public HeatmapDock( Widget parent ) : base( parent )
	{
		MinimumWidth = 280f;
		Layout = Layout.Column();
		Layout.Margin = 8f;
		Layout.Spacing = 4f;

		Label Section( string text )
		{
			var label = new Label( text, this );
			label.SetStyles( "font-weight: bold; margin-top: 6px;" );
			return label;
		}

		Layout.Add( Section( "Connection" ) );
		Layout.Add( new Label( "Secret API Key (sk_…)", this ) );
		_apiKey = new SecretLineEdit( this )
		{
			Secret = ProjectCookie.Get( $"{CookiePrefix}.apikey", "" ),
		};
		_apiKey.SecretEdited = () => ProjectCookie.Set( $"{CookiePrefix}.apikey", _apiKey.Secret );
		Layout.Add( _apiKey );

		_ingestUrl = AddField( "Ingest URL" );
		_ingestUrl.Text = ProjectCookie.Get( $"{CookiePrefix}.ingesturl", DefaultIngestUrl );
		_ingestUrl.TextEdited += _ => ProjectCookie.Set( $"{CookiePrefix}.ingesturl", _ingestUrl.Text );

		Layout.Add( Section( "Query" ) );
		Layout.Add( new Label( "Scene", this ) );
		_scene = new ComboBox( this );
		Layout.Add( _scene );
		var loadScenes = new Button( "Load Scenes", this );
		loadScenes.Clicked = () => _ = LoadScenesAsync();
		Layout.Add( loadScenes );

		_eventType = AddField( "Event Type (optional)" );
		_fromDate = AddField( "From (yyyy-mm-dd)" );
		_fromDate.Text = DateTime.UtcNow.AddDays( -30 ).ToString( DateFormat );
		_toDate = AddField( "To (yyyy-mm-dd)" );
		_toDate.Text = DateTime.UtcNow.ToString( DateFormat );
		_voxelSize = AddField( "Voxel Size" );
		_voxelSize.Text = "64";

		_metricKey = AddField( "Metric Key (optional)" );
		Layout.Add( new Label( "Metric Aggregation", this ) );
		_metricAgg = new ComboBox( this );
		foreach ( var agg in new[] { "avg", "max", "min", "sum" } )
			_metricAgg.AddItem( agg );
		Layout.Add( _metricAgg );

		Layout.Add( Section( "Rendering" ) );
		Layout.Add( new Label( "Mode", this ) );
		_renderMode = new ComboBox( this );
		_renderMode.AddItem( "Fog" );
		_renderMode.AddItem( "Cubes" );
		_renderMode.ItemChanged += RebuildOverlay;
		Layout.Add( _renderMode );

		_density = AddSlider( "Density", 0.1f, 20f, 3.5f );
		_falloff = AddSlider( "Falloff", 0.25f, 5f, 1.8f );
		_steps = AddSlider( "Steps", 16f, 256f, 96f );

		var buttons = Layout.AddRow();
		buttons.Spacing = 4f;
		var refresh = new Button( "Refresh", this );
		refresh.Clicked = () => _ = RefreshAsync();
		buttons.Add( refresh );
		var toggle = new Button( "Show / Hide", this );
		toggle.Clicked = ToggleVisible;
		buttons.Add( toggle );

		_status = new Label( "", this );
		_status.WordWrap = true;
		Layout.Add( _status );
		Layout.AddStretchCell();
	}

	LineEdit AddField( string title )
	{
		Layout.Add( new Label( title, this ) );
		var edit = new LineEdit( this );
		Layout.Add( edit );
		return edit;
	}

	FloatSlider AddSlider( string title, float min, float max, float value )
	{
		Layout.Add( new Label( title, this ) );
		var slider = new FloatSlider( this )
		{
			Minimum = min,
			Maximum = max,
			Value = value,
		};
		// Fog look params live in material attributes — update in place, no
		// re-fetch. (Steps/density/falloff don't affect the cubes mesh.)
		slider.OnValueEdited = () => _overlay.UpdateLook( _density.Value, _falloff.Value, _steps.Value );
		Layout.Add( slider );
		return slider;
	}

	void SetStatus( string text ) => _status.Text = text;

	SpatialApiClient CreateClient() => new( _ingestUrl.Text, _apiKey.Secret );

	async System.Threading.Tasks.Task LoadScenesAsync()
	{
		try
		{
			SetStatus( "Loading scenes…" );
			var response = await CreateClient().GetScenesAsync( _fromDate.Text.Trim(), _toDate.Text.Trim() );
			var selected = _scene.CurrentText;
			_scene.Clear();
			foreach ( var scene in response.Scenes )
				_scene.AddItem( scene.Scene );
			if ( !string.IsNullOrEmpty( selected ) && response.Scenes.Any( s => s.Scene == selected ) )
				_scene.TrySelectNamed( selected );
			SetStatus( response.Scenes.Count == 0 ? "No scenes with spatial data in this range." : $"{response.Scenes.Count} scene(s)." );
		}
		catch ( SpatialApiException e )
		{
			SetStatus( e.StatusCode == 401 ? "Invalid API key." : $"Scene fetch failed: {e.Message}" );
		}
	}

	async System.Threading.Tasks.Task RefreshAsync()
	{
		if ( _fetching )
			return;

		var scene = _scene.CurrentText;
		if ( string.IsNullOrWhiteSpace( scene ) )
		{
			SetStatus( "Pick a scene first (Load Scenes)." );
			return;
		}
		if ( !float.TryParse( _voxelSize.Text, out var voxelSize ) || voxelSize <= 0f )
		{
			SetStatus( "Voxel size must be a positive number." );
			return;
		}

		var metricKey = _metricKey.Text.Trim();
		var useMetric = !string.IsNullOrEmpty( metricKey );

		try
		{
			_fetching = true;
			SetStatus( "Fetching voxels…" );
			var response = await CreateClient().GetVoxelsAsync( new SpatialApiClient.VoxelsQuery
			{
				Scene = scene,
				VoxelSize = voxelSize,
				From = _fromDate.Text.Trim(),
				To = _toDate.Text.Trim(),
				EventType = _eventType.Text.Trim(),
				MetricKey = useMetric ? metricKey : null,
				MetricAgg = _metricAgg.CurrentText,
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
				SetStatus( "No data for this query." );
				return;
			}
			if ( _grid == null )
			{
				_overlay.Hide();
				SetStatus( $"Grid exceeds {DensityGridBuilder.MaxGridCells:N0} cells — try a larger voxel size." );
				return;
			}

			_visible = true;
			RebuildOverlay();
			SetStatus( response.Truncated
				? $"{_voxels.Count} voxels (result limit hit — data truncated)."
				: $"{_voxels.Count} voxels." );
		}
		catch ( SpatialApiException e )
		{
			SetStatus( e.StatusCode switch
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

	void ToggleVisible()
	{
		_visible = !_visible;
		if ( _visible && _grid != null )
			RebuildOverlay();
		else
			_overlay.Hide();
	}

	void RebuildOverlay()
	{
		if ( !_visible || _grid == null )
			return;

		var world = SceneEditorSession.Active?.Scene?.SceneWorld;
		if ( world == null )
		{
			SetStatus( "No active editor scene." );
			return;
		}

		var mode = _renderMode.CurrentText == "Cubes" ? HeatmapRenderMode.Cubes : HeatmapRenderMode.Fog;
		_overlay.Show( world, mode, _grid, _voxels, _fetchedVoxelSize, _fetchedUseMetric,
			_density.Value, _falloff.Value, _steps.Value );
	}

	public override void OnDestroyed()
	{
		_overlay.Dispose();
		base.OnDestroyed();
	}
}
