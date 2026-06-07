using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Turns a SteamID into a stable, anonymous, one-way player id. The raw SteamID
/// (PII) is never sent. Uses s&amp;box's whitelisted <c>string.Md5()</c> extension.
/// </summary>
public static class AnonymousId
{
	const string Salt = "sbox-analytics:";

	public static string Hash( ulong steamId )
	{
		if ( steamId == 0 )
			return "";

		return (Salt + steamId).Md5();
	}
}
