# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

# Set the working directory in the container
WORKDIR /app

# Copy the solution file and restore dependencies
COPY *.sln .
COPY . .
RUN dotnet restore

# Build the solution
RUN dotnet publish -c Release -o out

# Use the official ASP.NET runtime image for the final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Set the working directory in the container
WORKDIR /app

# Copy the published output from the build image
COPY --from=build-env /app/out .

# Expose the port that your application will run on
EXPOSE 4011

# Define the command to run your application
CMD ["dotnet", "ClassIn.dll"]
