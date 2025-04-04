using System.Reflection;
using System.Diagnostics;

namespace Bastet.Services
{
    /// <summary>
    /// Implementation of IVersionService to provide application version information
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the VersionService class
    /// </remarks>
    /// <param name="environment">Web host environment</param>
    public class VersionService(IWebHostEnvironment environment) : IVersionService
    {

        /// <summary>
        /// Gets the current application version
        /// </summary>
        /// <returns>
        /// Returns "Development" in development environment,
        /// otherwise returns the actual version from the assembly.
        /// </returns>
        public string GetVersion()
        {
            if (environment.IsDevelopment())
            {
                return "Development";
            }

            // Get version set during build from the AssemblyInformationalVersion attribute
            // This typically contains the semantic version that was set during build with /p:Version=
            var versionAttribute = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            
            return versionAttribute?.InformationalVersion ?? "Development"; // Fallback version
        }
    }
}
