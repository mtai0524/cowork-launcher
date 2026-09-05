# 03 — Mô hình dữ liệu

## Quan hệ

```
CoworkWorkspace
├─ Settings : WorkspaceSettings
└─ Apps : List<ManagedApp>
          ├─ EnvironmentVariables : Dictionary<string,string>
          ├─ ConfigFiles : List<ConfigFileRef>
          └─ Schedule : ScheduleRule
                        ├─ Times : List<TimeSpan>
                        └─ DaysOfWeek : List<DayOfWeek>

AppRunRecord (lưu riêng trong history.json, liên kết qua AppId)
```

## `ManagedApp`

Một app do Cowork quản lý.

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `Id` | `Guid` | Khoá bất biến; giữ nguyên kể cả khi đổi tên |
| `Name` | `string` | Tên hiển thị |
| `Description` | `string` | Ghi chú tự do |
| `Group` | `string` | Nhóm để gom trên dashboard (vd `Backup`) |
| `ExecutablePath` | `string` | Đường dẫn `.exe`/`.bat`/`.cmd`/`.ps1`; hỗ trợ `%VAR%` |
| `Arguments` | `string` | Tham số dòng lệnh; hỗ trợ `%VAR%` |
| `WorkingDirectory` | `string` | Thư mục làm việc; trống ⇒ dùng thư mục chứa file chương trình |
| `EnvironmentVariables` | `Dictionary` | Biến môi trường **riêng cho tiến trình con**, không đụng hệ thống |
| `ConfigFiles` | `List<ConfigFileRef>` | Các file cấu hình thuộc app này |
| `Schedule` | `ScheduleRule` | Lịch chạy tự động |
| `Enabled` | `bool` | Tắt ⇒ không chạy tay lẫn theo lịch |
| `RunAsAdministrator` | `bool` | Chạy qua ShellExecute verb `runas` (bật UAC) |
| `CaptureOutput` | `bool` | Thu stdout/stderr; **không có tác dụng khi bật `RunAsAdministrator`** |
| `WindowStyle` | `AppWindowStyle` | `Normal` / `Minimized` / `Hidden` |
| `SingleInstance` | `bool` | Chặn khởi chạy nếu instance trước còn sống |
| `TimeoutMinutes` | `int` | Tự kill sau ngần này phút; `0` = không giới hạn |
| `KeepAlive` | `bool` | Tự khởi động lại khi tiến trình tự thoát (không áp dụng khi bấm Dừng) |
| `RestartDelaySeconds` | `int` | Chờ ngần này giây trước khi khởi động lại (mặc định `5`) |
| `MaxRestartsPerHour` | `int` | Trần số lần khởi động lại mỗi giờ, chạm trần thì bỏ cuộc; `0` = không giới hạn (mặc định `10`) |
| `Order` | `int` | Thứ tự hiển thị **và** thứ tự khi bấm "Chạy tất cả" |
| `LastScheduledRunAt` | `DateTimeOffset?` | Lần cuối **scheduler** kích hoạt — dùng để tính mốc kế tiếp |
| `LastRunAt` | `DateTimeOffset?` | Lần cuối chạy (kể cả chạy tay) |
| `LastExitCode` | `int?` | Mã thoát lần chạy gần nhất |

> `LastScheduledRunAt` và `LastRunAt` tách riêng có lý do: chạy tay lúc 10h **không** làm mất mốc
> lịch 14h. Nếu dùng chung một trường, bấm chạy tay sẽ vô tình huỷ lần chạy tự động kế tiếp.

## `ScheduleRule`

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `Enabled` | `bool` | Tắt lịch mà không mất cấu hình đã đặt |
| `Kind` | `ScheduleKind` | `Manual` / `DailyAtTimes` / `Interval` / `OnCoworkStartup` |
| `Times` | `List<TimeSpan>` | Mốc giờ trong ngày (dùng cho `DailyAtTimes`) |
| `Interval` | `TimeSpan` | Chu kỳ lặp (dùng cho `Interval`) |
| `DaysOfWeek` | `List<DayOfWeek>` | Ngày được phép chạy; **rỗng = mọi ngày** |
| `WindowStart` / `WindowEnd` | `TimeSpan?` | Cửa sổ giờ cho `Interval`; `null` = không giới hạn |
| `CatchUpMissedRun` | `bool` | Chạy bù nếu tới hạn lúc Cowork đang tắt |

Hai phương thức chính, đều là hàm thuần:

- `GetNextOccurrence(after)` — mốc kế tiếp sau một thời điểm (hiển thị "Chạy kế tiếp").
- `GetPreviousOccurrence(now)` — mốc gần nhất đã qua (dùng để xét tới hạn).

## `ConfigFileRef`

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `Id` | `Guid` | Khoá |
| `DisplayName` | `string` | Tên trên tab; trống ⇒ dùng tên file |
| `Path` | `string` | Tuyệt đối, hoặc **tương đối so với `WorkingDirectory` của app** |
| `Format` | `ConfigFormat` | `Auto` / `Json` / `Ini` / `Xml` / `PlainText` |
| `BackupOnSave` | `bool` | Tạo `.cowork.bak` trước mỗi lần ghi (mặc định bật) |

`Auto` suy ra định dạng theo phần mở rộng:

| Phần mở rộng | Định dạng |
|---|---|
| `.json` `.jsonc` | JSON |
| `.ini` `.cfg` `.conf` `.properties` `.env` | INI |
| `.xml` `.config` `.xaml` `.csproj` | XML |
| còn lại | Văn bản thuần |

Đường dẫn tương đối rất hữu ích khi bạn nhân bản app: đổi `WorkingDirectory` là toàn bộ file config
trỏ sang thư mục mới, không phải sửa từng dòng.

## `AppRunRecord`

Lưu trong `history.json`, tối đa 5000 bản ghi (trần cứng, ngoài cấu hình số ngày giữ).

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `Id` | `Guid` | Khoá; ghi lại cùng `Id` = **cập nhật**, không tạo bản ghi mới |
| `AppId` / `AppName` | `Guid` / `string` | Tên được chụp lại tại thời điểm chạy, nên đổi tên app sau này không làm sai lịch sử |
| `Trigger` | `RunTrigger` | `Manual` / `Schedule` / `Startup` / `RunAll` / `KeepAlive` |
| `Outcome` | `RunOutcome` | `Running` / `Succeeded` / `Failed` / `Cancelled` / `TimedOut` / `NotStarted` |
| `StartedAt` / `FinishedAt` | `DateTimeOffset` | Mốc thời gian |
| `ExitCode` | `int?` | Mã thoát; `0` = thành công |
| `ProcessId` | `int` | PID |
| `Error` | `string?` | Lý do lỗi ở dạng đọc được |

## `WorkspaceSettings`

| Trường | Mặc định | Ý nghĩa |
|---|---|---|
| `SchedulerEnabled` | `true` | Bật bộ đếm lịch nền |
| `MinimizeToTray` | `true` | Bấm X ⇒ thu nhỏ xuống khay thay vì thoát |
| `StartWithWindows` | `false` | Ghi khoá `HKCU\...\Run` (không cần quyền admin) |
| `OutputBufferLines` | `2000` | Số dòng output giữ trong RAM mỗi app |
| `HistoryRetentionDays` | `30` | Số ngày giữ lịch sử chạy |
| `NotifyOnFailure` | `true` | Bong bóng ở khay khi app chạy lỗi hoặc không giữ chạy được |
| `Theme` | `Dark` | `Dark` / `Light` / `Midnight` / `HighContrast` |
| `Language` | `Vietnamese` | `Vietnamese` / `English` |

## Schema `workspace.json`

Enum ghi thành chuỗi, `TimeSpan` ghi dạng `"HH:mm:ss"` — file đọc và sửa tay được.

```json
{
  "Version": 1,
  "Apps": [
    {
      "Id": "3f2a1c88-9b0e-4d7a-9c11-2b6e5a4d0e13",
      "Name": "Sao lưu CSDL",
      "Description": "Backup toàn phần, chạy trước giờ hành chính",
      "Group": "Backup",
      "ExecutablePath": "C:\\tools\\backup\\run.bat",
      "Arguments": "--full --quiet",
      "WorkingDirectory": "C:\\tools\\backup",
      "EnvironmentVariables": {
        "DB_HOST": "10.0.0.5",
        "DB_USER": "backup_svc"
      },
      "ConfigFiles": [
        {
          "Id": "a1b2c3d4-0000-0000-0000-000000000001",
          "DisplayName": "Kết nối CSDL",
          "Path": "config\\database.ini",
          "Format": "Ini",
          "BackupOnSave": true
        }
      ],
      "Schedule": {
        "Enabled": true,
        "Kind": "DailyAtTimes",
        "Times": ["02:15:00"],
        "Interval": "01:00:00",
        "DaysOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
        "CatchUpMissedRun": true
      },
      "Enabled": true,
      "RunAsAdministrator": false,
      "CaptureOutput": true,
      "WindowStyle": "Hidden",
      "SingleInstance": true,
      "TimeoutMinutes": 90,
      "KeepAlive": false,
      "RestartDelaySeconds": 5,
      "MaxRestartsPerHour": 10,
      "Order": 0,
      "CreatedAt": "2026-09-04T14:00:00+07:00",
      "LastScheduledRunAt": "2026-09-04T02:15:00+07:00",
      "LastRunAt": "2026-09-04T02:15:00+07:00",
      "LastExitCode": 0
    }
  ],
  "Settings": {
    "SchedulerEnabled": true,
    "MinimizeToTray": true,
    "StartWithWindows": false,
    "OutputBufferLines": 2000,
    "HistoryRetentionDays": 30,
    "NotifyOnFailure": true,
    "Theme": "Dark",
    "Language": "Vietnamese"
  }
}
```

### Sửa tay `workspace.json`

Được, nhưng **đóng Cowork trước** — nếu không, lần lưu kế tiếp sẽ ghi đè. Cowork vá các trường
thiếu khi nạp (`null` ⇒ giá trị mặc định), nên bạn không cần điền đủ mọi khoá. Nếu file sai cú pháp,
nó bị đổi tên thành `workspace.json.corrupt-<timestamp>` và Cowork khởi động với workspace rỗng.

### Nâng cấp schema

`Version` hiện là `1`. Khi cấu trúc thay đổi trong tương lai, bước migration sẽ đặt trong
`JsonWorkspaceStore.Normalize` — nơi hiện đang vá các trường `null`.
