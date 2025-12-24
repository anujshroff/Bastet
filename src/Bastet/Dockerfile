FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled

WORKDIR /app

COPY . .

USER app

ENTRYPOINT [ "dotnet", "Bastet.dll" ]
