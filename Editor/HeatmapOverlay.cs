using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Noot.Analytics.Editor;

public enum HeatmapRenderMode
{
	Fog,
	Cubes,
}

/// <summary>
/// Owns the editor-scene visualization: one SceneObject in the active editor
/// scene world, either a raymarched fog box or a vertex-colored cube mesh.
/// Created on Show, destroyed on Hide; look-only changes update attributes.
/// </summary>
public sealed class HeatmapOverlay : IDisposable
{
	const float NormalizePercentile = 0.95f;
	const float MinCubeScale = 0.25f;

	// Fixed raymarch step count for the fog volume. Was an exposed dock control;
	// removed from the UI, baked here so the shader still gets a sane value.
	const float FogSteps = 96f;

	SceneObject _sceneObject;
	Texture _volumeTexture;

	public bool IsVisible => _sceneObject.IsValid();

	/// <summary>Rebuild the overlay from a fetched dataset. Destroys any previous object.</summary>
	public void Show(
		SceneWorld world,
		HeatmapRenderMode mode,
		DensityGrid grid,
		IReadOnlyList<HeatmapVoxel> voxels,
		float voxelSize,
		bool useMetric,
		float density,
		float falloff )
	{
		Hide();

		if ( world == null || grid == null )
			return;

		if ( mode == HeatmapRenderMode.Fog )
			ShowFog( world, grid, density, falloff );
		else
			ShowCubes( world, voxels, voxelSize, useMetric );
	}

	/// <summary>Push fog look attributes without re-fetching or rebuilding.</summary>
	public void UpdateLook( float density, float falloff )
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.Attributes.Set( "Density", density );
		_sceneObject.Attributes.Set( "Falloff", falloff );
	}

	public void Hide()
	{
		_sceneObject?.Delete();
		_sceneObject = null;
		_volumeTexture?.Dispose();
		_volumeTexture = null;
	}

	public void Dispose() => Hide();

	void ShowFog( SceneWorld world, DensityGrid grid, float density, float falloff )
	{
		var mins = grid.Center - grid.Size / 2f;
		var maxs = grid.Center + grid.Size / 2f;

		_volumeTexture = Texture.CreateVolume( grid.Nx, grid.Ny, grid.Nz, ImageFormat.I8 )
			.WithName( "analytics_heatmap_density" )
			.WithData( grid.Data )
			.Finish();

		var material = Material.FromShader( "shaders/heatmap_fog.shader" );
		var model = BuildBoxModel( material, mins, maxs );

		_sceneObject = new SceneObject( world, model, Transform.Zero )
		{
			Flags = { CastShadows = false },
		};
		_sceneObject.Attributes.Set( "DensityTexture", _volumeTexture );
		_sceneObject.Attributes.Set( "VolumeMins", mins );
		_sceneObject.Attributes.Set( "VolumeSize", grid.Size );
		_sceneObject.Attributes.Set( "Steps", FogSteps );
		UpdateLook( density, falloff );
	}

	/// <summary>
	/// Draw each trajectory as its own polyline in a single <see cref="SceneLineObject"/>.
	/// Colors step through hue by the golden ratio so adjacent paths stay visually
	/// distinct. Replaces any previous overlay object (modes are exclusive).
	/// </summary>
	public void ShowLines( SceneWorld world, IReadOnlyList<TrajectoryDto> trajectories, float lineWidth )
	{
		Hide();

		if ( world == null || trajectories == null || trajectories.Count == 0 )
			return;

		var lines = CreateLineObject( world );

		for ( var i = 0; i < trajectories.Count; i++ )
		{
			var points = trajectories[i].Points;
			if ( points == null || points.Count < 2 )
				continue;

			var color = PathColor( i );
			lines.StartLine();
			foreach ( var p in points )
				lines.AddLinePoint( new Vector3( p.X, p.Y, p.Z ), color, lineWidth );
			lines.EndLine();
		}

		_sceneObject = lines;
	}

	/// <summary>
	/// Draw pre-computed navmesh routes (the estimated walked path between samples)
	/// as colored polylines with periodic chevrons marking the direction of travel.
	/// Routes are open — the last point is never joined back to the first.
	/// </summary>
	public void ShowNavRoutes( SceneWorld world, IReadOnlyList<List<Vector3>> routes,
		float lineWidth, float arrowSpacing )
	{
		Hide();

		if ( world == null || routes == null || routes.Count == 0 )
			return;

		var lines = CreateLineObject( world );

		for ( var i = 0; i < routes.Count; i++ )
		{
			var route = routes[i];
			if ( route == null || route.Count < 2 )
				continue;

			var color = PathColor( i );
			lines.StartLine();
			foreach ( var p in route )
				lines.AddLinePoint( p, color, lineWidth );
			lines.EndLine();

			AddArrows( lines, route, color, lineWidth, arrowSpacing );
		}

		_sceneObject = lines;
	}

	// A SceneLineObject has no material by default and renders the engine error
	// texture (the red/black stripes). The built-in line material with a white
	// color map lets the per-point vertex colors come through.
	static SceneLineObject CreateLineObject( SceneWorld world )
	{
		var material = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();
		material.Set( "Color", Texture.White );

		var lines = new SceneLineObject( world )
		{
			Flags = { CastShadows = false },
			Opaque = true,
			Lighting = false,
		};
		lines.Material = material;
		lines.Attributes.SetCombo( "D_BLEND", 0 );
		return lines;
	}

	// Drop a forward-pointing chevron every `spacing` units along the route so
	// the direction of travel reads at a glance.
	static void AddArrows( SceneLineObject lines, List<Vector3> route, Color color, float width, float spacing )
	{
		if ( spacing <= 0f )
			return;

		const float ArrowSize = 16f;
		var walked = 0f;
		var nextAt = spacing;

		for ( var i = 0; i < route.Count - 1; i++ )
		{
			var a = route[i];
			var seg = route[i + 1] - a;
			var segLen = seg.Length;
			if ( segLen < 0.01f )
				continue;

			var dir = seg / segLen;
			while ( nextAt <= walked + segLen )
			{
				AddChevron( lines, a + dir * (nextAt - walked), dir, ArrowSize, color, width );
				nextAt += spacing;
			}
			walked += segLen;
		}
	}

	// A "^" pointing along `dir`: back-left → tip → back-right.
	static void AddChevron( SceneLineObject lines, Vector3 pos, Vector3 dir, float size, Color color, float width )
	{
		var right = Vector3.Cross( dir, Vector3.Up );
		if ( right.LengthSquared < 0.0001f )
			right = Vector3.Forward;
		right = right.Normal;

		var tip = pos + dir * size;
		var backLeft = pos - dir * size + right * size;
		var backRight = pos - dir * size - right * size;

		lines.StartLine();
		lines.AddLinePoint( backLeft, color, width );
		lines.AddLinePoint( tip, color, width );
		lines.AddLinePoint( backRight, color, width );
		lines.EndLine();
	}

	// Distinct categorical color per path. The golden-ratio hue step keeps
	// consecutive paths far apart on the wheel instead of bunching.
	static Color PathColor( int index )
	{
		var hue = (index * 0.618_033_988f) % 1f;
		return HsvToColor( hue, 0.85f, 1f );
	}

	static Color HsvToColor( float h, float s, float v )
	{
		var sector = (h - MathF.Floor( h )) * 6f;
		var c = v * s;
		var x = c * (1f - MathF.Abs( sector % 2f - 1f ));
		var m = v - c;

		float r, g, b;
		if ( sector < 1f ) { r = c; g = x; b = 0f; }
		else if ( sector < 2f ) { r = x; g = c; b = 0f; }
		else if ( sector < 3f ) { r = 0f; g = c; b = x; }
		else if ( sector < 4f ) { r = 0f; g = x; b = c; }
		else if ( sector < 5f ) { r = x; g = 0f; b = c; }
		else { r = c; g = 0f; b = x; }

		return new Color( r + m, g + m, b + m );
	}

	void ShowCubes( SceneWorld world, IReadOnlyList<HeatmapVoxel> voxels, float voxelSize, bool useMetric )
	{
		if ( voxels == null || voxels.Count == 0 )
			return;

		// Normalize against the 95th percentile so one outlier voxel doesn't
		// wash every other cube transparent — same policy as the fog grid.
		var intensities = voxels
			.Select( v => useMetric ? (v.Value ?? 0f) : v.Count )
			.Where( i => i > 0f )
			.OrderBy( i => i )
			.ToList();
		if ( intensities.Count == 0 )
			return;
		var scale = intensities[Math.Min( intensities.Count - 1, (int)MathF.Floor( intensities.Count * NormalizePercentile ) )];
		if ( scale <= 0f )
			return;

		var verts = new List<Vertex>( voxels.Count * 8 );
		var indices = new List<int>( voxels.Count * 36 );
		var bounds = BBox.FromPositionAndSize( new Vector3( voxels[0].X, voxels[0].Y, voxels[0].Z ), voxelSize );

		foreach ( var v in voxels )
		{
			var intensity = useMetric ? (v.Value ?? 0f) : v.Count;
			var t = Math.Clamp( intensity / scale, 0f, 1f );
			var color = InfernoColor( t ).WithAlpha( Math.Clamp( t, 0.04f, 0.85f ) );

			// Scale each cube with intensity so the heatmap reads as a density
			// cloud rather than a wall of uniform blocks.
			var half = voxelSize * (MinCubeScale + (1f - MinCubeScale) * t) / 2f;

			var center = new Vector3( v.X, v.Y, v.Z );
			bounds = bounds.AddBBox( BBox.FromPositionAndSize( center, voxelSize ) );
			AddCube( verts, indices, center, half, color );
		}

		var material = Material.FromShader( "shaders/heatmap_cubes.shader" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( verts.Count, Vertex.Layout, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = bounds;

		var model = Model.Builder.AddMesh( mesh ).Create();
		_sceneObject = new SceneObject( world, model, Transform.Zero )
		{
			Flags = { CastShadows = false },
		};
	}

	static Model BuildBoxModel( Material material, Vector3 mins, Vector3 maxs )
	{
		var verts = new List<Vertex>( 8 );
		var indices = new List<int>( 36 );
		AddBox( verts, indices, mins, maxs, Color.White );

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( verts.Count, Vertex.Layout, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = new BBox( mins, maxs );

		return Model.Builder.AddMesh( mesh ).Create();
	}

	static void AddCube( List<Vertex> verts, List<int> indices, Vector3 center, float half, Color color )
		=> AddBox( verts, indices, center - half, center + half, color );

	static void AddBox( List<Vertex> verts, List<int> indices, Vector3 mins, Vector3 maxs, Color color )
	{
		var baseIndex = verts.Count;
		for ( var i = 0; i < 8; i++ )
		{
			var corner = new Vector3(
				(i & 1) == 0 ? mins.x : maxs.x,
				(i & 2) == 0 ? mins.y : maxs.y,
				(i & 4) == 0 ? mins.z : maxs.z );
			verts.Add( new Vertex( corner, Vector3.Up, Vector3.Forward, Vector2.Zero ) { Color = color } );
		}

		// 12 triangles over the 8 shared corners; winding is irrelevant since
		// both heatmap shaders set their own cull state.
		ReadOnlySpan<int> cube = stackalloc int[]
		{
			0, 1, 3, 0, 3, 2, // -z
			4, 6, 7, 4, 7, 5, // +z
			0, 4, 5, 0, 5, 1, // -y
			2, 3, 7, 2, 7, 6, // +y
			0, 2, 6, 0, 6, 4, // -x
			1, 5, 7, 1, 7, 3, // +x
		};
		foreach ( var idx in cube )
			indices.Add( baseIndex + idx );
	}

	// Inferno colormap polynomial, mirroring the fog shader's ramp so the two
	// render modes read the same.
	static Color InfernoColor( float t )
	{
		static float Poly( float t, float c0, float c1, float c2, float c3, float c4, float c5, float c6 ) =>
			c0 + t * (c1 + t * (c2 + t * (c3 + t * (c4 + t * (c5 + t * c6)))));

		var r = Poly( t, 0.00021894037f, 0.10651342f, 11.602493f, -41.703996f, 77.162936f, -71.319428f, 25.131126f );
		var g = Poly( t, 0.0016510046f, 0.56395643f, -3.9728540f, 17.436399f, -33.402359f, 32.626064f, -12.242669f );
		var b = Poly( t, -0.019480898f, 3.9327124f, -15.942394f, 44.354145f, -81.807309f, 73.209520f, -23.070325f );
		return new Color( Math.Clamp( r, 0f, 1f ), Math.Clamp( g, 0f, 1f ), Math.Clamp( b, 0f, 1f ) );
	}
}
