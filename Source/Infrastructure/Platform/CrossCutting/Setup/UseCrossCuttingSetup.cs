using Microsoft.AspNetCore.Builder;
using Scheduling.Setup;

namespace CrossCutting.Setup;

public static class UseCrossCuttingSetup
{
    public static WebApplication UseCrossCuttingSetupPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseSchedulingSetupPipeline();
    }
}