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
| **Chạy** | Chạy tay từng app hoặc "Chạy tất cả", khởi động lại một phát, chặn chạy chồng, tự dừng khi quá giờ; kết quả chấm theo danh sách **mã thoát thành công** của từng app (robocopy trả 1 vẫn là xong việc) |
| **Dừng lịch sự** | Đóng cửa sổ chính với app GUI, gửi **Ctrl+C** cho app console (node, python, .bat), chờ số giây ân hạn của app rồi mới kill cả cây tiến trình |
| **Giữ luôn chạy** | App tự thoát hay crash thì Cowork khởi động lại sau vài giây; có trần số lần mỗi giờ để không lặp vô tận với app đã hỏng hẳn |
| **Bắt app treo** | Thăm dò cổng TCP hoặc URL định kỳ, và theo dõi output (im lặng quá lâu, hoặc in ra mẫu như `FATAL`); treo thì Cowork dừng app rồi để keep-alive / thử lại lo phần chạy lại |
| **Thử lại khi lỗi** | Job kết thúc lỗi hoặc quá giờ thì chạy lại sau N giây, tối đa M lần; chỉ báo ở khay khi hết lượt vẫn lỗi. Bấm Dừng thì không thử lại |
| **Tìm chương trình** | Quét thư mục dò `.exe`/`.bat`/`.cmd`/`.ps1`, xếp hạng tin cậy; file `.ps1` được tự bọc qua `powershell.exe` |
| **Cấu hình app con** | Tham số dòng lệnh, thư mục làm việc, biến môi trường riêng, kiểu cửa sổ, quyền admin |
| **Tìm file cấu hình** | Quét thư mục của app, tự dò file nào là cấu hình — kể cả file **không có đuôi** như `~/.config/app/config` — chấm điểm tin cậy Cao/Vừa/Thấp, bỏ qua `node_modules`/`bin`/file lock |
| **Sửa file cấu hình** | Mở JSON / INI / .env / XML / App.config ngay trong Cowork — dạng bảng khoá-giá trị hoặc sửa nguồn, có backup tự động |
| **Lịch chạy** | Mốc giờ cố định trong ngày, chu kỳ lặp, chạy khi mở Cowork; lọc theo ngày trong tuần; chạy bù khi lỡ |
| **Theo dõi** | Nhật ký output realtime, lịch sử chạy có mã thoát và thời lượng, log ra file, thông báo ở khay khi app chạy lỗi |
| **Chạy nền** | Thu nhỏ xuống khay hệ thống, khởi động cùng Windows |
| **Quản lý từ xa** | Nhiều máy nối ra một hub web: xem trạng thái, bấm Chạy / Dừng / Khởi động lại từ trình duyệt — xem [docs/06](docs/06-quan-ly-tu-xa.md) |

## Chạy thử

```bash
dotnet build                                  # build toàn bộ solution
dotnet test                                   # toàn bộ test
dotnet run --project src/Cowork.App           # mở ứng dụng
dotnet run --project src/Cowork.Hub           # hub quản lý từ xa (đặt mật khẩu trong appsettings.json trước)
```

Yêu cầu: Windows + .NET 8 SDK (`dotnet --list-sdks` phải có bản 8.x trở lên).

## Cấu trúc

```
Cowork.sln
├─ src/Cowork.Core/     Model, service, bộ đọc-ghi config — không phụ thuộc WPF
│   ├─ Models/          ManagedApp, ScheduleRule, ConfigFileRef, AppRunRecord…
│   ├─ Configuration/   JsonConfigEditor, IniConfigEditor, XmlConfigEditor, ConfigFileScanner, ProgramScanner…
│   ├─ Services/        ProcessManager, DailyScheduler, KeepAliveSupervisor, RetrySupervisor, HealthMonitor, LogPruner, JsonWorkspaceStore…
│   └─ Validation/      AppValidator
├─ src/Cowork.App/      WPF, MVVM (CommunityToolkit.Mvvm)
│   ├─ ViewModels/      MainViewModel, AppViewModel, ConfigFileViewModel, ScanConfigViewModel, ScanProgramViewModel…
│   ├─ Converters/      Converter cho binding
│   ├─ Localization/    Nhãn đa ngôn ngữ cho XAML
│   └─ Themes/          Kiểu dáng điều khiển + bốn bảng màu
├─ src/Cowork.Remote/   Hợp đồng dữ liệu, sổ máy, client SignalR — dùng chung cho agent và hub, không WPF/ASP.NET
├─ src/Cowork.Hub/      Hub quản lý từ xa: ASP.NET Core + Blazor Server, chạy được trên Linux
├─ tests/Cowork.Tests/  xUnit — lịch, config editor, lưu trữ, chạy tiến trình thật, hub trong tiến trình
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
| `logs\app-<id>-YYYYMMDD.log` | Output đầy đủ của từng app; file cũ hơn số ngày đặt trong Thiết lập được tự xoá |

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [docs/01-tong-quan.md](docs/01-tong-quan.md) | Bài toán, phạm vi, khái niệm |
| [docs/02-kien-truc.md](docs/02-kien-truc.md) | Kiến trúc, luồng dữ liệu, quyết định thiết kế |
| [docs/03-mo-hinh-du-lieu.md](docs/03-mo-hinh-du-lieu.md) | Model và schema `workspace.json` |
| [docs/04-huong-dan-su-dung.md](docs/04-huong-dan-su-dung.md) | Hướng dẫn dùng theo từng tình huống |
| [docs/05-lo-trinh.md](docs/05-lo-trinh.md) | Giới hạn hiện tại và hướng phát triển |
