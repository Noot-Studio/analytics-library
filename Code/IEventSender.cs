using System.Collections.Generic;
using System.Threading.Tasks;

namespace Noot.Analytics;

/// <summary>Sends a batch to the ingestion API. Abstracted so the core is testable.</summary>
public interface IEventSender
{
	/// <summary>Returns true if the batch was accepted (HTTP 202).</summary>
	Task<bool> SendAsync( List<AnalyticsEvent> batch, string publishableKey, string ingestUrl );
}
