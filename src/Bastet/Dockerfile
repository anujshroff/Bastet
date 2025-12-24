FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra

WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY . .

USER app

ENTRYPOINT [ "dotnet", "Bastet.dll" ]
