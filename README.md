# PF_Project_4P1W

Monorepo for `4 Pics 1 Word` with:

- `src/auth-api` - ASP.NET Core auth service with JWT login/register/me and seeded player/admin users
- `src/resource-api` - ASP.NET Core resource/gameplay/CMS service with local JSON persistence and local file uploads
- `src/web-app` - React + Vite player app and admin CMS

## Repository layout

```text
PF_Project_4P1W/
  src/
    auth-api/
    resource-api/
    web-app/
  docs/
```

## Seed accounts

- Admin: `admin@4pics.local` / `Admin123!`
- Player: `player@4pics.local` / `Player123!`

## Run locally

### 1. Auth API

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project .\src\auth-api\AuthApi.csproj
```

Runs on `http://localhost:5001`

### 2. Resource API

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project .\src\resource-api\ResourceApi.csproj
```

Runs on `http://localhost:5002`

### 3. React app

```powershell
cd .\src\web-app
Copy-Item .env.example .env -Force
npm.cmd install
npm.cmd run dev
```

Runs on `http://localhost:5173`

## Delivered features

- JWT-based auth with `player` and `admin` roles
- React protected routes and admin-only CMS routes
- Randomized published pack listing
- Randomized next puzzle selection with cooldown avoidance
- Guess normalization with score updates
- Player progress summary
- Admin CMS for images, tags, puzzles, and packs
- Local dev file uploads with image URLs returned by the API
- Demo-ready seeded puzzle pack data

## Notes

- Persistence is JSON-backed in each API's `App_Data` folder for fast local setup.
- Uploads are stored in `src/resource-api/wwwroot/uploads`.
- A local GitHub Project board cannot be created from this repository alone; use the provided iteration breakdown from the assignment when creating it on GitHub.
