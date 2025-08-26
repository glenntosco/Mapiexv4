# P4W Integration - Simplified Version

## Overview
The application has been simplified to automatically run all active operations defined in `config.json` without requiring command-line arguments.

## How to Run

### Option 1: Using .NET CLI
```bash
dotnet run
```

### Option 2: Using Batch File (Windows)
```batch
run.bat
```

### Option 3: Using PowerShell
```powershell
.\run.ps1
```

### Option 4: Direct Execution (after building)
```bash
dotnet build
./bin/Debug/net9.0/P4WIntegration.exe
```

## Configuration

All settings are now controlled through `config.json`:

1. **Company Settings**: The first company in the `Companies` array will be used
2. **Active Operations**: Operations with `"Active": true` and `"RunOnStartup": true` in the `Schedules` section will be executed

### Example config.json structure:
```json
{
  "Companies": [
    {
      "CompanyName": "DEMO_MAPIEX_AVIATION",
      "SapB1": { ... },
      "P4WClientName": "MAPIEX",
      ...
    }
  ],
  "Schedules": [
    {
      "Name": "ProductSync",
      "Active": true,
      "RunOnStartup": true,
      ...
    }
  ]
}
```

## What Changed?

1. **No command-line arguments required** - The app reads everything from config.json
2. **Automatic company selection** - Uses the first company in the config
3. **Schedule-based execution** - Runs all operations marked as active with RunOnStartup=true
4. **Simplified execution** - Just run the app without any parameters

## Managing Operations

To control which operations run:
1. Edit `config.json`
2. Set `"Active": true/false` for each schedule
3. Set `"RunOnStartup": true/false` to control initial execution

## Multiple Companies

If you need to support multiple companies in the future, you can:
1. Set an environment variable: `P4W_COMPANY=COMPANY_NAME`
2. Or modify the code to loop through all companies
3. Or create separate config files for each company

## Logs

Logs are automatically created in:
- Console output
- `logs/p4w-{CompanyName}-{Date}.txt`
- SQL Server IntegrationLogs table