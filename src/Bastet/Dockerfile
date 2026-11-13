FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra

WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY . .

USER app

ENTRYPOINT [ "dotnet", "Bastet.dll" ]
