# Mello-Roos on Windows

Run the rate extraction tool on a **Windows 10/11** desktop with PowerShell.

**First run installs everything automatically** — Poppler (`pdftotext`, `pdftoppm`, `pdfinfo`) and Tesseract OCR — via `winget`, or direct download if `winget` is unavailable.

---

## 1. Install .NET 10 SDK (one-time)

Download and install: [.NET 10 SDK](https://dotnet.microsoft.com/download)

Verify:

```powershell
dotnet --version
```

That is the only manual prerequisite. PDF tools install on the first command you run.

---

## 2. Open the project folder

Open PowerShell in the folder containing `MelloRoos.sln` (for example `C:\Projects\Mello-Roos`):

```powershell
cd C:\Projects\Mello-Roos
dotnet build MelloRoos.sln
mkdir out -Force
```

---

## 3. Set API key (one-time)

### Persist (recommended)

Survives reboots. **Close and reopen PowerShell** after running these.

```powershell
# OpenAI (default)
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-proj-...", "User")

# Optional model override (if check-keys shows a narrow allowlist)
[System.Environment]::SetEnvironmentVariable("OPENAI_MODEL", "gpt-5", "User")

# Gemini (use --provider gemini on extract)
# [System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "AIza...", "User")

# Claude (use --provider claude on extract)
# [System.Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

### Current session only

Lost when you close the window:

```powershell
$env:OPENAI_API_KEY = "sk-proj-..."
```

### Verify

```powershell
if ($env:OPENAI_API_KEY) { "OPENAI_API_KEY is set" } else { "OPENAI_API_KEY is NOT set — reopen PowerShell if you just set User env vars" }
dotnet run --project src/MelloRoos.csproj -- check-keys
```

---

## 4. Run extract (first run installs PDF tools)

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  --debt-id 123 `
  --run-date 2026-08-18 `
  --save-json out/extraction.json `
  -o out/rates.sql
```

**First run only:** you may see:

```
Mello-Roos: installing PDF tools (first run on Windows)...
Installing Poppler via winget (oschwartz10612.Poppler)...
Installing Tesseract OCR via winget (tesseract-ocr.tesseract)...
PDF tools installed successfully.
```

Admin approval may appear for `winget`. Takes a few minutes.

Subsequent runs skip installation if tools are already present.

Helper script:

```powershell
.\scripts\extract.ps1 `
  -Pdf "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  -DebtId 123 `
  -RunDate "2026-08-18"
```

**Scanned PDF** — add `--force-ocr` or `-ForceOcr` on the script.

---

## 5. Review and generate SQL

1. Open `out\extraction.json`
2. Verify rates against the PDF
3. If **Review required**, edit JSON then:

```powershell
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json `
  --debt-id 123 `
  --run-date 2026-08-18 `
  -o out/rates.sql
```

---

## Verify setup

```powershell
dotnet run --project src/MelloRoos.csproj -- check-deps
dotnet run --project src/MelloRoos.csproj -- check-keys
```

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `winget not found` | Install [App Installer](https://apps.microsoft.com/detail/9nblggh4nns1) from the Microsoft Store, then re-run |
| Setup failed / UAC blocked | Run PowerShell as Administrator once, or install [Poppler](https://github.com/oschwartz10612/poppler-windows/releases) + [Tesseract](https://github.com/UB-Mannheim/tesseract/wiki) manually |
| `'dotnet' is not recognized` | Install .NET 10 SDK, restart PowerShell |
| Tools installed but not found | Open a **new** PowerShell window (PATH refresh), run `check-deps` |
| Path with spaces | Keep quotes: `"C:\Docs\my rma.pdf"` |
| `insufficient_quota` | Add API billing at platform.openai.com, or `--provider gemini` |

### Where tools are installed

| Tool | Typical location |
|------|------------------|
| Poppler (winget) | WinGet packages folder, added to PATH |
| Poppler (fallback download) | `%LOCALAPPDATA%\MelloRoos\tools\poppler\...\Library\bin` |
| Tesseract | `C:\Program Files\Tesseract-OCR` |

---

## Optional: publish a standalone `.exe`

Still auto-installs PDF tools on first run. Requires .NET runtime unless self-contained:

```powershell
dotnet publish src/MelloRoos.csproj -c Release -r win-x64 -o publish/win-x64
.\publish\win-x64\MelloRoos.exe extract "your-rma.pdf" --debt-id 123 --save-json out/extraction.json -o out/rates.sql
```

---

See [`INSTRUCTIONS.md`](../INSTRUCTIONS.md) for the full workflow.
