using System;
using ICSharpCode.Core;

namespace UnoDevelop.Services;

internal sealed class UnoAnalyticsMonitor : IAnalyticsMonitor
{
    private static readonly NullFeature _null = new();

    public void TrackException(Exception exception) { }
    public IAnalyticsMonitorTrackedFeature TrackFeature(string featureName, string activationMethod = null) => _null;
    public IAnalyticsMonitorTrackedFeature TrackFeature(Type featureClass, string featureName = null, string activationMethod = null) => _null;

    sealed class NullFeature : IAnalyticsMonitorTrackedFeature
    {
        public void EndTracking() { }
    }
}
