FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra

WORKDIR /app

COPY --chmod=755 . .

USER app

CMD [ "dotnet", "Bastet.dll" ]
