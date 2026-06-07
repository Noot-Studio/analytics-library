using Editor;
using Sandbox;
using Noot.Analytics;

/// <summary>Editor convenience: drop an AnalyticsComponent into the open scene.</summary>
public static class AnalyticsMenu
{
	[Menu( "Editor", "Analytics/Add to scene" )]
	public static void AddToScene()
	{
		var scene = SceneEditorSession.Active?.Scene;
		if ( scene is null )
		{
			EditorUtility.DisplayDialog( "Analytics", "Open a scene first." );
			return;
		}

		var go = scene.CreateObject();
		go.Name = "Analytics";
		go.Components.Create<AnalyticsComponent>();
	}
}
