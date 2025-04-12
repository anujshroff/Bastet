using System.Reflection;

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

            // Get version set during build
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null
                ? (version.Major == 0 && version.Minor == 0 && version.Build == 0
                    ? "Alpha"
                    : $"{version.Major}.{version.Minor}.{version.Build}")
                : "Development"; // Fallback version
        }
    }
}