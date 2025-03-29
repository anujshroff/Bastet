namespace Bastet.Services
{
    /// <summary>
    /// Service to provide application version information
    /// </summary>
    public interface IVersionService
    {
        /// <summary>
        /// Gets the current application version
        /// </summary>
        /// <returns>
        /// Returns "Development" in development environment,
        /// otherwise returns the actual version set during build.
        /// </returns>
        string GetVersion();
    }
}
