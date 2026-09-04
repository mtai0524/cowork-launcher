# Cowork

Ứng dụng desktop Windows (C# / WPF) để **quản lý những app bạn phải chạy mỗi ngày**: khai báo một
lần, rồi bấm chạy hoặc để Cowork tự chạy theo lịch — và sửa file cấu hình của chúng ngay trong app,
không cần mở Notepad tìm đường dẫn.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Cowork   ▶ Chạy   ■ Dừng  │  Chạy tất cả  Dừng tất cả  Kiểm tra lịch│
├──────────────────┬──────────────────────────────────────────────────┤
│ Danh sách app    │ Tổng quan │ Biến MT │ File cấu hình │ Lịch chạy…  │
│                  │                                                  │
│ ● Sao lưu CSDL   │  Tên app     [ Sao lưu CSDL          ]           │
│   Đang chạy      │  Chương trình[ C:\tools\backup.bat   ] [Chọn…]   │
│   Kế tiếp: 02:15 │  Tham số     [ --full --verbose      ]           │
│                  │                                                  │
│ ● Báo cáo sáng   │  ☑ Không chạy chồng   ☑ Thu nhật ký output       │
│   Chạy lần cuối… │                                                  │
└──────────────────┴──────────────────────────────────────────────────┘
```

## Làm được gì

| Nhóm | Chức năng |
|---|---|
| **Quản lý app** | Thêm / sửa / xoá / nhân bản, gom nhóm, sắp thứ tự, bật-tắt từng app, tìm kiếm |
| **Chạy** | Chạy tay từng app hoặc "Chạy tất cả", dừng lịch sự rồi mới kill, chặn chạy chồng, tự dừng khi quá giờ |
| **Tìm chương trình** | Quét thư mục dò `.exe`/`.bat`/`.cmd`/`.ps1`, xếp hạng tin cậy; file `.ps1` được tự bọc qua `powershell.exe` |
| **Cấu hình app con** | Tham số dòng lệnh, thư mục làm việc, biến môi trường riêng, kiểu cửa sổ, quyền admin |
| **Tìm file cấu hình** | Quét thư mục của app, tự dò file nào là cấu hình — kể cả file **không có đuôi** như `~/.config/app/config` — chấm điểm tin cậy Cao/Vừa/Thấp, bỏ qua `node_modules`/`bin`/file lock |
| **Sửa file cấu hình** | Mở JSON / INI / .env / XML / App.config ngay trong Cowork — dạng bảng khoá-giá trị hoặc sửa nguồn, có backup tự động |
| **Lịch chạy** | Mốc giờ cố định trong ngày, chu kỳ lặp, chạy khi mở Cowork; lọc theo ngày trong tuần; chạy bù khi lỡ |
| **Theo dõi** | Nhật ký output realtime, lịch sử chạy có mã thoát và thời lượng, log ra file |
| **Chạy nền** | Thu nhỏ xuống khay hệ thống, khởi động cùng Windows |

## Chạy thử

```bash
dotnet build                                  # build toàn bộ solution
dotnet test                                   # 118 test
dotnet run --project src/Cowork.App           # mở ứng dụng
```

Yêu cầu: Windows + .NET 8 SDK (`dotnet --list-sdks` phải có bản 8.x trở lên).

## Cấu trúc

```
Cowork.sln
├─ src/Cowork.Core/     Model, service, bộ đọc-ghi config — không phụ thuộc WPF
│   ├─ Models/          ManagedApp, ScheduleRule, ConfigFileRef, AppRunRecord…
│   ├─ Configuration/   JsonConfigEditor, IniConfigEditor, XmlConfigEditor, ConfigFileScanner, ProgramScanner…
│   ├─ Services/        ProcessManager, DailyScheduler, JsonWorkspaceStore…
│   └─ Validation/      AppValidator
├─ src/Cowork.App/      WPF, MVVM (CommunityToolkit.Mvvm)
│   ├─ ViewModels/      MainViewModel, AppViewModel, ConfigFileViewModel, ScanConfigViewModel, ScanProgramViewModel…
│   ├─ Converters/      Converter cho binding
│   └─ Themes/          Theme tối
├─ tests/Cowork.Tests/  xUnit — lịch, config editor, lưu trữ, chạy tiến trình thật
└─ docs/                Tài liệu chi tiết
```

Tách `Cowork.Core` khỏi WPF là chủ ý: mọi logic quyết định (khi nào tới giờ chạy, ghi giá trị nào
vào file config) đều test được mà không cần dựng cửa sổ.

## Dữ liệu người dùng

Nằm trong `%APPDATA%\Cowork`:

| File | Nội dung |
|---|---|
| `workspace.json` | Danh sách app + thiết lập. Sao lưu file này là sao lưu toàn bộ cấu hình. |
| `history.json` | Lịch sử các lần chạy |
| `backups\workspace-YYYYMMDD.json` | Ảnh chụp workspace đầu mỗi ngày, giữ 10 bản gần nhất |
| `logs\cowork-YYYYMMDD.log` | Log hoạt động của Cowork |
| `logs\app-<id>-YYYYMMDD.log` | Output đầy đủ của từng app |

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [docs/01-tong-quan.md](docs/01-tong-quan.md) | Bài toán, phạm vi, khái niệm |
| [docs/02-kien-truc.md](docs/02-kien-truc.md) | Kiến trúc, luồng dữ liệu, quyết định thiết kế |
| [docs/03-mo-hinh-du-lieu.md](docs/03-mo-hinh-du-lieu.md) | Model và schema `workspace.json` |
| [docs/04-huong-dan-su-dung.md](docs/04-huong-dan-su-dung.md) | Hướng dẫn dùng theo từng tình huống |
| [docs/05-lo-trinh.md](docs/05-lo-trinh.md) | Giới hạn hiện tại và hướng phát triển |
