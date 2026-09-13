# Build and run the EDNexus Twitch Extension Backend Service (EBS) as a standalone container.
#
# Build (from the repository root):
#   docker build -f src/EDNexus.Ebs/Dockerfile -t ednexus-ebs .
#
# Run:
#   docker run -p 8787:8787 \
#     -e Twitch__ClientId=<client-id> \
#     -e Twitch__ExtensionId=<extension-id> \
#     -e Twitch__ExtensionSecret=<base64-extension-secret> \
#     -e Ebs__Port=8787 \
#     ednexus-ebs

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/EDNexus.Ebs/EDNexus.Ebs.csproj src/EDNexus.Ebs/
RUN dotnet restore src/EDNexus.Ebs/EDNexus.Ebs.csproj

COPY src/EDNexus.Ebs/ src/EDNexus.Ebs/
RUN dotnet publish src/EDNexus.Ebs/EDNexus.Ebs.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV Ebs__Port=8787
EXPOSE 8787

ENTRYPOINT ["dotnet", "EDNexus.Ebs.dll"]
