<p align="center"><img src="assets/logo.png" width="96" alt="โลโก้ MediaForge"></p>

<h1 align="center">MediaForge</h1>

<p align="center">
แปลง ตัดต่อ และดาวน์โหลดวิดีโอ/เสียงบน Windows<br>
พัฒนาโดย <a href="https://siammods.com">Siammods.com</a> · © 2026 Siammods.com<br>
<a href="README.md">English</a> · <b>ภาษาไทย</b>
</p>

MediaForge เป็นโปรแกรม desktop บน Windows เขียนด้วย **C# 14 / .NET 10 (LTS) + WPF (Fluent theme)**
ส่วนงานหนักส่งให้ **FFmpeg** และ **yt-dlp** หน้าโปรแกรมเลือกได้ทั้งภาษาไทยและอังกฤษ
มีธีมสว่าง/มืดตาม Windows และใช้สี accent ที่ตั้งไว้ใน Windows

## ฟีเจอร์

| หน้า | ทำอะไรได้ |
|---|---|
| **แปลงไฟล์** | วิดีโอ → เสียง, เสียง → เสียง (MP3, M4A/AAC, Opus, OGG, WMA, FLAC, WAV, ALAC, AIFF), "ดึงเสียงต้นฉบับ" แบบไม่ re-encode · วิดีโอ → วิดีโอ (MP4/MKV/MOV/WebM/AVI, H.264, H.265, VP9, AV1, MPEG-4) เลือกคุณภาพ ความละเอียด และ FPS ได้ · เปลี่ยนแค่ container (remux) · ใช้การ์ดจอช่วยแปลงได้ (NVIDIA NVENC / Intel QSV / AMD AMF ถ้าเครื่องรองรับ) · ลากไฟล์หรือทั้งโฟลเดอร์มาวางได้ (batch) |
| **ตัดต่อ** | ตัดช่วง (แบบเร็วไม่ re-encode หรือแบบแม่นยำ) · รวมไฟล์ (แบบเร็ว หรือ re-encode ให้รวมไฟล์ต่างขนาด/ต่างรูปแบบได้) · ปรับความดังให้สม่ำเสมอ EBU R128 แบบ 2-pass หรือเพิ่ม/ลด dB · ปรับความเร็ว 0.25x–4x โดยเสียงไม่เพี้ยน · ทำ GIF คุณภาพสูง |
| **YouTube** | วางได้หลายลิงก์ ทั้งคลิปและ playlist · วิเคราะห์ลิงก์แล้วเลือกเฉพาะคลิปที่ต้องการ · โหลดเป็นวิดีโอ (เลือกความละเอียด และ MP4/MKV/WebM) หรือเสียง (MP3/M4A/Opus/FLAC/WAV) · ฝังปก ข้อมูลเพลง และซับ · ใส่ลำดับหน้าชื่อไฟล์และแยกโฟลเดอร์ตาม playlist |
| **แท็ก / ปก / ซับ** | ดูข้อมูลไฟล์ (ffprobe) · แก้แท็กเพลง · เปลี่ยน/ลบ/บันทึกภาพปก (MP3, M4A, FLAC) · ดึงซับออกเป็น SRT/ASS/VTT · ใส่ซับแบบเปิด/ปิดได้ · ฝังซับลงในภาพถาวร · ซับไทยแบบ TIS-620/Windows-874 แปลงให้อัตโนมัติ |
| **ตั้งค่า** | หน้าตา (ภาษา ธีม) · ดูเวอร์ชัน FFmpeg / yt-dlp ดาวน์โหลดหรืออัปเดตได้จากในโปรแกรม · JS runtime ของ yt-dlp · จำนวนงานที่ทำพร้อมกัน · อัปเดตโปรแกรม · เกี่ยวกับและเครดิต |

ทุกงานเข้า **คิวงาน** ด้านล่าง รันพร้อมกันได้ (แยก limit ระหว่างงานแปลงกับงานดาวน์โหลด) มี progress และเวลาที่เหลือ
ยกเลิกได้ (ปิด process ทั้งหมดและลบไฟล์ที่ทำค้าง) กดลองใหม่ เปิดโฟลเดอร์ผลลัพธ์ หรือดู log ได้

## ติดตั้ง

**ต้องมี:** Windows 10 (1809) ขึ้นไป หรือ Windows 11 แบบ x64 ไม่ต้องลง .NET

### ทางที่ 1: ตัวติดตั้งจาก GitHub Releases

1. โหลด `MediaForge-Setup-<เวอร์ชัน>.exe` จาก [release ล่าสุด](https://github.com/ThunSaen04/MediaForge/releases/latest)
2. เปิดไฟล์ ระบบติดตั้งให้เฉพาะผู้ใช้คนนี้ (`%LOCALAPPDATA%\Programs\MediaForge`) ไม่ต้องใช้สิทธิ์ admin
3. เลือกภาษาของตัวติดตั้ง ไทย/English และเลือกได้ว่าจะสร้างทางลัดบน desktop หรือไม่

> **ขึ้น "Windows protected your PC"?** ตัวติดตั้งยังไม่มี code signing ทำให้ SmartScreen ของ Microsoft อาจเตือน
> กด **More info → Run anyway** ตรวจได้ว่าไฟล์เป็นของจริงด้วยค่า SHA-256 ที่แนบไว้ในแต่ละ release
> (`MediaForge-Setup-<เวอร์ชัน>.exe.sha256`)

### ทางที่ 2: winget

```powershell
winget install Siammods.MediaForge
```

อัปเดตภายหลังด้วย `winget upgrade Siammods.MediaForge`
(ใช้ได้เมื่อแพ็กเกจผ่านการตรวจและเข้า [winget community repository](https://github.com/microsoft/winget-pkgs) แล้ว)

### เครื่องมือเสริม

- **FFmpeg**: ถ้าไม่มีใน PATH โปรแกรมจะเสนอให้ดาวน์โหลด (BtbN build) ไปไว้ที่ `%LOCALAPPDATA%\MediaForge\tools`
- **yt-dlp**: โปรแกรมดาวน์โหลดให้เอง และอัปเดตได้จากหน้าตั้งค่า
- **Deno หรือ Node.js 22+**: yt-dlp รุ่นใหม่ต้องใช้ JavaScript runtime เพื่อดึง format ของ YouTube ได้ครบ
  โปรแกรมหาให้อัตโนมัติ (Deno ก่อน แล้วค่อย Node.js) หรือกดดาวน์โหลด Deno จากหน้าตั้งค่า

## การอัปเดตโปรแกรม

MediaForge เช็กเวอร์ชันใหม่จาก [GitHub Releases](https://github.com/ThunSaen04/MediaForge/releases) ตอนเปิดโปรแกรม (ไม่เกินวันละครั้ง)
และกดเช็กเองได้ที่ **ตั้งค่า → เกี่ยวกับ → ตรวจหาอัปเดต** ปิดการเช็กอัตโนมัติได้ที่จุดเดียวกัน

เมื่อมีเวอร์ชันใหม่ จะขึ้นแถบแจ้งเตือนพร้อมเลขเวอร์ชันและรายการสิ่งที่เปลี่ยน กด **อัปเดตตอนนี้** แล้วโปรแกรมจะ
ดาวน์โหลดตัวติดตั้ง → ตรวจ SHA-256 ให้ตรงกับ release → ปิดตัวเอง → ติดตั้งทับเวอร์ชันเดิม (ค่าตั้งค่ายังอยู่) → เปิดเวอร์ชันใหม่ให้
ถ้า checksum ไม่ตรง จะไม่ติดตั้งอะไรเลย

## Build จาก source

ต้องมี [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
dotnet run --project src/MediaForge
```

สร้าง `MediaForge.exe` ไฟล์เดียวไว้ที่ `publish\`:

```powershell
.\publish.ps1                      # self-contained ~67 MB รันได้โดยไม่ต้องลง .NET
.\publish.ps1 -FrameworkDependent  # ไฟล์เล็ก แต่เครื่องปลายทางต้องมี .NET 10 Desktop Runtime
```

สร้างตัวติดตั้ง (ต้องมี [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1              # -> installer\Output\MediaForge-Setup-<เวอร์ชัน>.exe
```

### Smoke tests

สร้างไฟล์ตัวอย่างด้วย ffmpeg แล้วเรียกทุกฟีเจอร์ผ่านโค้ดจริง จากนั้นตรวจผลด้วย ffprobe
(มีทั้งชื่อไฟล์ภาษาไทย, เสียง 5.1, ซับ TIS-620, GPU encoder และการยกเลิกงาน)

```powershell
dotnet run --project tests/MediaForge.SmokeTests -- <โฟลเดอร์ทำงาน>                # แปลง/ตัดต่อ/แท็ก/ซับ
dotnet run --project tests/MediaForge.SmokeTests -- <โฟลเดอร์ทำงาน> --youtube      # รวมทดสอบ YouTube (ต้องใช้เน็ต)
dotnet run --project tests/MediaForge.SmokeTests -- <โฟลเดอร์ทำงาน> --filter=merge # รันเฉพาะ test ที่ชื่อมีคำนี้
```

## ออกเวอร์ชันใหม่

1. แก้เลข `<Version>` ใน `src/MediaForge/MediaForge.csproj` แล้ว commit และ push
2. ปล่อย GitHub release ผู้ใช้ที่ติดตั้งไว้จะได้รับแจ้งอัปเดตจาก release นี้:
   ```powershell
   .\release.ps1 -Notes "สิ่งที่เปลี่ยน"     # หรือ -NotesFile notes.md · ใส่ -PreRelease ถ้ายังไม่อยากให้ผู้ใช้เห็น
   ```
3. อัปเดต winget (ต้องมี `winget install Microsoft.WingetCreate`):
   ```powershell
   .\winget\winget-manifest.ps1 -Submit     # สร้างและตรวจ manifest แล้วเปิด PR ไปที่ microsoft/winget-pkgs
   ```

โลโก้และไอคอนทั้งหมดสร้างจาก `assets\logo.png` (เจนด้วย GPT ผ่าน Codex CLI)
สร้างไอคอนใหม่ด้วย `pwsh .\assets\make-icons.ps1`

## โครงสร้างโปรเจกต์

```
src/MediaForge/
  Core/           ไม่ผูกกับ UI: ProcessRunner, Tools (หา/ติดตั้ง ffmpeg, yt-dlp), MediaProbe (ffprobe),
                  FFmpeg (รัน + แปลง progress), Converter (สร้างคำสั่ง ffmpeg ทุกฟีเจอร์), YtDlp, Updater, Job, Presets, AppInfo
  ViewModels/     MVVM: หน้าละตัว + QueueViewModel (scheduler ของคิวงาน) + UpdateViewModel
  Views/          XAML ของแต่ละหน้า
  Controls/       FileBox (ช่อง path ที่ browse และลากไฟล์มาวางได้)
  Localization/   Loc + Strings (ข้อความ TH/EN ทั้งหมด), markup extension {l:Tr Key}
  Themes/         Styles.xaml: card, nav item, segmented tabs, text styles (ใช้สีจาก Fluent theme)
  UI/             Dialogs, converters, Icon.Glyph, หน้าต่าง log/ดาวน์โหลด, ThemeHelper
  Assets/         MediaForge.ico, logo.png
tests/MediaForge.SmokeTests/   console app ทดสอบ end-to-end
installer/        สคริปต์ Inno Setup
winget/           สคริปต์สร้าง winget manifest (โฟลเดอร์ manifests/ สร้างอัตโนมัติ)
assets/           ไฟล์โลโก้ต้นฉบับ + make-icons.ps1
```

## ไฟล์ที่โปรแกรมสร้าง

- ค่าตั้งค่า: `%APPDATA%\MediaForge\settings.json`
- เครื่องมือที่ดาวน์โหลด: `%LOCALAPPDATA%\MediaForge\tools\`
- ไฟล์ชั่วคราวและไฟล์อัปเดต: `%TEMP%\MediaForge\`
- ไฟล์ YouTube (ค่าเริ่มต้น): `%USERPROFILE%\Downloads\MediaForge\`

ตอนถอนการติดตั้ง ค่าตั้งค่าและเครื่องมือที่โหลดมาจะยังอยู่ ถ้าต้องการลบให้หมด ให้ลบโฟลเดอร์ข้างบนเอง

## หมายเหตุ

- ถ้าดาวน์โหลด YouTube ไม่ได้ ให้กด "อัปเดต yt-dlp" ในหน้าตั้งค่าก่อน เพราะ YouTube เปลี่ยนระบบบ่อย
- FFmpeg แบบ static build ขนาดใหญ่ (เช่น gyan full ~217 MB) ใช้เวลาเปิด process ราว 1 วินาทีต่อครั้ง
  เลยเห็นเวลาเริ่มต้นเล็กน้อยในทุกงาน ไม่ได้มาจากตัวโปรแกรม
- ดาวน์โหลดเฉพาะเนื้อหาที่ตัวเองมีสิทธิ์ใช้ ตามเงื่อนไขของ YouTube และกฎหมายลิขสิทธิ์

## เครดิต

พัฒนาโดย **[Siammods.com](https://siammods.com)**

MediaForge ใช้โปรแกรม open source เหล่านี้ (โปรแกรมดาวน์โหลดแยกตอนใช้งาน) แต่ละโปรแกรมเป็นของผู้พัฒนาและใช้ license ของตัวเอง:

- [FFmpeg](https://ffmpeg.org): LGPL/GPL
- [yt-dlp](https://github.com/yt-dlp/yt-dlp): Unlicense
- [Deno](https://deno.com): MIT
