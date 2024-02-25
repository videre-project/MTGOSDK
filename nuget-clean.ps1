# Clear the 3.x+ cache (use either command)
dotnet nuget locals http-cache --clear

# Clear the global packages folder (use either command)
dotnet nuget locals global-packages --clear

# Clear the temporary cache (use either command)
dotnet nuget locals temp --clear

# Clear the plugins cache (use either command)
dotnet nuget locals plugins-cache --clear

# Clear all caches (use either command)
dotnet nuget locals all --clear
