using System;
using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Declarative event tracking. Put on a method to emit when it is called, or on a
/// property to emit when it changes. Pure sugar over <see cref="Analytics.Track"/>;
/// requires the core to be initialized (otherwise Track no-ops).
/// </summary>
[AttributeUsage( AttributeTargets.Method | AttributeTargets.Property )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Instance | CodeGeneratorFlags.Static,
	"Noot.Analytics.TrackAttribute.OnInvoked" )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.Instance | CodeGeneratorFlags.Static,
	"Noot.Analytics.TrackAttribute.OnSet" )]
public sealed class TrackAttribute : Attribute
{
	/// <summary>Event name. Null/empty uses the member name.</summary>
	public string? Name { get; }

	/// <summary>Opt-in: capture method arguments as event properties. Ignored on properties.</summary>
	public bool Params { get; set; }

	public TrackAttribute( string? name = null ) => Name = name;

	/// <summary>Method-wrap callback (invoked by s&amp;box codegen).</summary>
	public static void OnInvoked( WrappedMethod m, params object[] args )
	{
		m.Resume?.Invoke();

		var attr = m.GetAttribute<TrackAttribute>();
		var type = string.IsNullOrEmpty( attr?.Name ) ? m.MethodName : attr!.Name!;
		var declaringType = m.Object?.GetType();
		var props = attr is { Params: true } && declaringType is not null
			? CaptureArgs( m.MethodIdentity, declaringType, args )
			: null;

		Analytics.Track( type, props );
	}

	/// <summary>Property-set-wrap callback (invoked by s&amp;box codegen).</summary>
	public static void OnSet<T>( WrappedPropertySet<T> p )
	{
		var getter = p.Getter;
		var current = getter is not null ? getter() : default;
		var changed = !EqualityComparer<T>.Default.Equals( current, p.Value );
		p.Setter?.Invoke( p.Value );
		if ( !changed )
			return;

		var attr = p.GetAttribute<TrackAttribute>();
		var type = string.IsNullOrEmpty( attr?.Name ) ? p.PropertyName : attr!.Name!;
		Analytics.Track( type, new Dictionary<string, object> { ["value"] = (object)p.Value! } );
	}

	/// <summary>
	/// Map positional arg values to a property dictionary. Resolves parameter names
	/// via TypeLibrary by method identity; falls back to arg0/arg1... Skips null args.
	/// </summary>
	public static Dictionary<string, object>? CaptureArgs( int methodIdentity, Type declaringType, object[] args )
	{
		if ( args is null || args.Length == 0 )
			return null;

		var names = ResolveParamNames( methodIdentity, declaringType, args.Length );
		var props = new Dictionary<string, object>( args.Length );
		for ( var i = 0; i < args.Length; i++ )
		{
			if ( args[i] is null )
				continue;
			props[names[i]] = args[i];
		}

		return props.Count > 0 ? props : null;
	}

	static string[] ResolveParamNames( int methodIdentity, Type declaringType, int count )
	{
		var names = new string[count];
		for ( var i = 0; i < count; i++ )
			names[i] = $"arg{i}";

		try
		{
			var td = TypeLibrary.GetType( declaringType );
			foreach ( var method in td.Methods )
			{
				if ( method.Identity != methodIdentity )
					continue;

				var parameters = method.Parameters;
				for ( var i = 0; i < count && i < parameters.Length; i++ )
					names[i] = parameters[i].Name ?? names[i];
				break;
			}
		}
		catch
		{
			// TypeLibrary unavailable or shape differs → positional names.
		}

		return names;
	}
}
