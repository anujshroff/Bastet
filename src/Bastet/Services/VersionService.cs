using System.Reflection;

namespace Bastet.Services
{

    public class VersionService(IWebHostEnvironment environment) : IVersionService
    {

        public string GetVersion()
        {
            if (environment.IsDevelopment())
            {
                return "Development";
            }

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null
                ? (version.Major == 0 && version.Minor == 0 && version.Build == 0
                    ? "Alpha"
                    : $"{version.Major}.{version.Minor}.{version.Build}")
                : "Development";
        }
    }
}
