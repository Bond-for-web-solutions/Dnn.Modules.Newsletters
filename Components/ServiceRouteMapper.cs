using DotNetNuke.Web.Api;

namespace Dnn.Modules.Newsletters.Components
{
    /// <summary>
    /// Registers Web API routes for the Newsletters module.
    /// </summary>
    public class ServiceRouteMapper : IServiceRouteMapper
    {
        /// <inheritdoc/>
        public void RegisterRoutes(IMapRoute mapRouteManager)
        {
            mapRouteManager.MapHttpRoute(
                "Newsletters",
                "default",
                "{controller}/{action}",
                new[] { "Dnn.Modules.Newsletters.Controllers" });
        }
    }
}
