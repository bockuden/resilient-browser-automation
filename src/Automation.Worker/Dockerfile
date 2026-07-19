FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /src

COPY Directory.Build.props global.json ResilientBrowserAutomation.sln ./
COPY src/Automation.Core/Automation.Core.csproj src/Automation.Core/
COPY src/Automation.Application/Automation.Application.csproj src/Automation.Application/
COPY src/Automation.Storage/Automation.Storage.csproj src/Automation.Storage/
COPY src/Automation.Playwright/Automation.Playwright.csproj src/Automation.Playwright/
COPY src/Automation.Worker/Automation.Worker.csproj src/Automation.Worker/

RUN dotnet restore src/Automation.Worker/Automation.Worker.csproj

COPY src ./src
RUN dotnet publish src/Automation.Worker/Automation.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

WORKDIR /app
COPY --from=build /app/publish ./

RUN cp Automation.Worker.runtimeconfig.json Microsoft.Playwright.runtimeconfig.json \
    && dotnet exec Microsoft.Playwright.dll install --with-deps chromium

RUN mkdir -p /data \
    && chown -R app:app /app /data /ms-playwright

USER app

ENTRYPOINT ["dotnet", "Automation.Worker.dll"]
