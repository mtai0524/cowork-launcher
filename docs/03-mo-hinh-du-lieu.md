# 03 — Mô hình dữ liệu

## Quan hệ

```
CoworkWorkspace
├─ Settings : WorkspaceSettings
└─ Apps : List<ManagedApp>
          ├─ EnvironmentVariables : Dictionary<string,string>
          ├─ ConfigFiles : List<ConfigFileRef>
          ├─ HealthCheck : HealthCheck
          │               └─ FailurePatterns : List<string>
          ├─ DependsOn : List<AppDependency>
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
| `HealthCheck` | `HealthCheck` | Cách nhận ra app treo |
| `DependsOn` | `List<AppDependency>` | Các app phải sẵn sàng trước, khi bấm "Chạy tất cả" |
| `SystemTriggers` | `List<SystemEventKind>` | Sự kiện của máy khiến app tự chạy: `Resume` / `SessionUnlock` / `NetworkAvailable`. Độc lập với `Schedule` |
| `SystemTriggerDelaySeconds` | `int` | Chờ ngần này giây sau sự kiện rồi mới chạy (mặc định `15`) |
| `Enabled` | `bool` | Tắt ⇒ không chạy tay lẫn theo lịch |
| `RunAsAdministrator` | `bool` | Chạy qua ShellExecute verb `runas` (bật UAC) |
| `CaptureOutput` | `bool` | Thu stdout/stderr; **không có tác dụng khi bật `RunAsAdministrator`** |
| `WindowStyle` | `AppWindowStyle` | `Normal` / `Minimized` / `Hidden` |
| `SingleInstance` | `bool` | Chặn khởi chạy nếu instance trước còn sống |
| `TimeoutMinutes` | `int` | Tự kill sau ngần này phút; `0` = không giới hạn |
| `KeepAlive` | `bool` | Tự khởi động lại khi tiến trình tự thoát (không áp dụng khi bấm Dừng) |
| `RestartDelaySeconds` | `int` | Chờ ngần này giây trước khi khởi động lại (mặc định `5`) |
| `MaxRestartsPerHour` | `int` | Trần số lần khởi động lại mỗi giờ, chạm trần thì bỏ cuộc; `0` = không giới hạn (mặc định `10`) |
| `RetryCount` | `int` | Số lần chạy lại khi một lần chạy kết thúc lỗi hoặc quá giờ; `0` = không thử lại (mặc định). Bị bỏ qua khi bật `KeepAlive` |
| `RetryDelaySeconds` | `int` | Chờ ngần này giây trước mỗi lần thử lại (mặc định `30`) |
| `SuccessExitCodes` | `List<int>` | Các mã thoát được coi là thành công (mặc định `[0]`); thiếu hoặc rỗng ⇒ vá về `[0]` khi nạp |
| `StopGraceSeconds` | `int` | Khi bấm Dừng: chờ ngần này giây cho app tự thoát sau khi được đóng cửa sổ / gửi Ctrl+C, rồi mới kill (mặc định `5`) |
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

## `HealthCheck`

Bịt đúng lỗ hổng của keep-alive: nó chỉ thấy tiến trình *thoát*, còn app sống mà đơ thì vẫn bị coi
là đang chạy.

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `Probe` | `HealthProbeKind` | `None` / `TcpPort` / `HttpGet` |
| `Target` | `string` | TCP: `8080` hoặc `host:8080`. HTTP: `http://localhost:8080/health` (mã 2xx = còn sống) |
| `IntervalSeconds` | `int` | Chu kỳ kiểm tra, dùng cho cả thăm dò lẫn xét im lặng (mặc định `30`) |
| `TimeoutSeconds` | `int` | Thăm dò quá ngần này giây không phản hồi ⇒ tính một lần lỗi (mặc định `5`) |
| `FailureThreshold` | `int` | Số lần thăm dò lỗi **liên tiếp** trước khi coi là treo (mặc định `3`) |
| `StartupGraceSeconds` | `int` | Bỏ qua mọi kiểm tra trong ngần này giây đầu, để app kịp lên (mặc định `30`) |
| `SilenceMinutes` | `int` | Không có dòng output nào trong ngần này phút ⇒ treo; `0` = tắt |
| `FailurePatterns` | `List<string>` | Regex (không phân biệt hoa thường) mà hễ xuất hiện trong output là app hỏng; mẫu sai cú pháp được so như chuỗi thường |

Một lần thăm dò thành công đặt lại bộ đếm về 0 — chỉ chuỗi lỗi *liên tiếp* mới tính. Watchdog theo
output cần `CaptureOutput` bật và `RunAsAdministrator` tắt; không thì Cowork không thấy dòng nào, và
`AppValidator` cảnh báo.

## `AppDependency`

| Trường | Kiểu | Ý nghĩa |
|---|---|---|
| `AppId` | `Guid` | `Id` của app phải chờ |
| `Wait` | `DependencyWait` | `Completed` = chờ nó chạy xong và thành công · `Running` = chỉ chờ nó lên |

`Completed` dành cho job (sao lưu xong mới nén). `Running` dành cho dịch vụ: app bật `KeepAlive`
không bao giờ "xong việc", nên chờ nó kết thúc là chờ mãi — `DependencyGraph.Validate` báo lỗi
trường hợp này và hàng đợi bỏ qua app phía sau thay vì treo.

Tự trỏ vào chính mình và `Guid.Empty` bị bỏ khi nạp. Xoá một app cũng dọn luôn các khai báo trỏ
tới nó.

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
| `Trigger` | `RunTrigger` | `Manual` / `Schedule` / `Startup` / `RunAll` / `KeepAlive` / `Remote` / `Retry` / `SystemEvent` |
| `Outcome` | `RunOutcome` | `Running` / `Succeeded` / `Failed` / `Cancelled` / `TimedOut` / `NotStarted` / `Unhealthy` |
| `StartedAt` / `FinishedAt` | `DateTimeOffset` | Mốc thời gian |
| `ExitCode` | `int?` | Mã thoát; thành công khi nằm trong `SuccessExitCodes` của app (mặc định chỉ `0`) |
| `ProcessId` | `int` | PID |
| `Error` | `string?` | Lý do lỗi ở dạng đọc được |
| `OutputLogFile` | `string?` | Tên file log chứa output của chính lần chạy này (trong thư mục `logs`). Chỉ tên, không đường dẫn — đổi chỗ thư mục dữ liệu vẫn mở lại được. Null khi app không thu output |

## `WorkspaceSettings`

| Trường | Mặc định | Ý nghĩa |
|---|---|---|
| `SchedulerEnabled` | `true` | Bật bộ đếm lịch nền |
| `MinimizeToTray` | `true` | Bấm X ⇒ thu nhỏ xuống khay thay vì thoát |
| `StartWithWindows` | `false` | Ghi khoá `HKCU\...\Run` (không cần quyền admin) |
| `OutputBufferLines` | `2000` | Số dòng output giữ trong RAM mỗi app |
| `HistoryRetentionDays` | `30` | Số ngày giữ lịch sử chạy |
| `LogRetentionDays` | `30` | Số ngày giữ file log trong thư mục `logs`; dọn lúc mở Cowork và đầu mỗi ngày |
| `NotifyOnFailure` | `true` | Bong bóng ở khay khi app chạy lỗi hoặc không giữ chạy được |
| `Notifications` | — | Cảnh báo ra ngoài máy (webhook / Telegram / email); xem [`NotificationSettings`](#notificationsettings). Công tắc riêng, không phụ thuộc `NotifyOnFailure` |
| `News` | — | Bảng tin hằng ngày: chủ đề, nguồn, tỉ lệ tin trong nước; xem [`NewsSettings`](#newssettings) |
| `Theme` | `Dark` | `Dark` / `Light` / `Midnight` / `HighContrast` |
| `Language` | `Vietnamese` | `Vietnamese` / `English` |
| `HubUrl` | `""` | Địa chỉ hub quản lý từ xa; trống = không kết nối |
| `HubToken` | `""` | Token của máy này, khớp với một dòng trong `Agents` của hub. **Lưu dạng thường** |

## `NotificationSettings`

Nằm trong `WorkspaceSettings.Notifications`. Kênh nào khai đủ thì kênh đó được gửi; gửi song song,
kênh hỏng không ảnh hưởng kênh còn lại.

| Trường | Mặc định | Ý nghĩa |
|---|---|---|
| `Enabled` | `false` | Công tắc chung; tắt thì không kênh nào gửi |
| `WebhookUrl` | `""` | POST JSON tới đây. Đủ điều kiện khi là URL http/https |
| `TelegramBotToken` · `TelegramChatId` | `""` | Đủ điều kiện khi **cả hai** có giá trị. Token **lưu dạng thường** |
| `SmtpHost` · `SmtpPort` · `SmtpUseSsl` | `""` · `587` · `true` | Máy chủ gửi mail |
| `SmtpUser` · `SmtpPassword` | `""` | Bỏ trống tài khoản ⇒ không xác thực. Mật khẩu **lưu dạng thường** |
| `EmailFrom` · `EmailTo` | `""` | Đủ điều kiện khi có host, người gửi và ít nhất một người nhận (cách nhau bằng `,` hoặc `;`) |
| `DedupeMinutes` | `5` | Khoảng lặng cho cảnh báo trùng khoá (cùng app + cùng tiêu đề). `0` = gửi mọi lần |

## `NewsSettings`

Nằm trong `WorkspaceSettings.News`. Chỉ thiết lập — bài đã tải nằm ở `news-cache.json`, một file
riêng vì nó tải lại được và thay đổi mỗi giờ, trong khi `workspace.json` thì hiếm khi.

| Trường | Mặc định | Ý nghĩa |
|---|---|---|
| `Enabled` | `true` | Tắt thì Cowork không gọi ra mạng để lấy tin |
| `Topics` | `["Ai","Agents","Technology"]` | `Ai` / `Agents` / `Technology` / `Programming` / `Startups` / `Security` / `Repos`. Danh sách rỗng ⇒ bảng tin rỗng, không phải "lấy tất cả" |
| `IncludeVietnam` | `true` | Có lấy báo trong nước không |
| `VietnamPercent` | `25` | Phần trăm chỗ để dành cho tin trong nước; phần còn lại là tin nước ngoài |
| `MaxItems` | `60` | Số bài tối đa của một lần đọc |
| `MaxAgeDays` | `3` | Bài cũ hơn ngần này ngày không hiện nữa |
| `MaxPerSource` | `6` | Trần số bài lấy từ một nguồn |
| `RefreshMinutes` | `60` | Bao lâu tự lấy lại. `0` = chỉ lấy khi bấm nút |
| `DisabledSourceIds` | `[]` | Mã những nguồn dựng sẵn đã tắt (`"hacker-news"`, `"genk"`…) |
| `CustomSources` | `[]` | Feed tự thêm; xem [`CustomNewsSource`](#customnewssource) |

Danh mục nguồn dựng sẵn nằm trong mã (`Cowork.Core/News/NewsCatalog.cs`), không nằm trong
`workspace.json`: thêm một báo là một thay đổi của bản phát hành, còn file người dùng chỉ giữ những
gì họ đã đổi so với mặc định.

## `CustomNewsSource`

| Trường | Ý nghĩa |
|---|---|
| `Name` | Tên hiển thị. Bỏ trống ⇒ lấy tên miền |
| `FeedUrl` | Địa chỉ RSS/Atom. Phải là http/https, nếu không nguồn bị bỏ qua |
| `Region` | `Global` / `Vietnam` — quyết định nó nằm bên nào của hạn mức |
| `Topics` | Ít nhất một chủ đề, nếu không nguồn bị bỏ qua |

Mã của một feed tự thêm sinh từ chính địa chỉ (`custom:<url>`) nên nó ổn định qua các lần chạy —
bộ nhớ đệm dựa vào đó để giữ lại bài của lần lấy trước.

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
      "DependsOn": [
        { "AppId": "a1b2c3d4-0000-0000-0000-000000000009", "Wait": "Running" }
      ],
      "SystemTriggers": ["Resume", "NetworkAvailable"],
      "SystemTriggerDelaySeconds": 15,
      "HealthCheck": {
        "Probe": "HttpGet",
        "Target": "http://localhost:8080/health",
        "IntervalSeconds": 30,
        "TimeoutSeconds": 5,
        "FailureThreshold": 3,
        "StartupGraceSeconds": 30,
        "SilenceMinutes": 0,
        "FailurePatterns": ["FATAL", "OutOfMemory"]
      },
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
      "RetryCount": 2,
      "RetryDelaySeconds": 60,
      "SuccessExitCodes": [0, 1],
      "StopGraceSeconds": 5,
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
    "LogRetentionDays": 30,
    "NotifyOnFailure": true,
    "Notifications": {
      "Enabled": true,
      "WebhookUrl": "https://hooks.slack.com/services/…",
      "TelegramBotToken": "",
      "TelegramChatId": "",
      "SmtpHost": "",
      "SmtpPort": 587,
      "SmtpUseSsl": true,
      "DedupeMinutes": 5
    },
    "HubUrl": "",
    "HubToken": "",
    "Theme": "Dark",
    "Language": "Vietnamese",
    "News": {
      "Enabled": true,
      "Topics": ["Ai", "Agents", "Technology"],
      "IncludeVietnam": true,
      "VietnamPercent": 25,
      "MaxItems": 60,
      "MaxAgeDays": 3,
      "MaxPerSource": 6,
      "RefreshMinutes": 60,
      "DisabledSourceIds": ["arxiv-ai"],
      "CustomSources": [
        {
          "Name": "Blog nội bộ",
          "FeedUrl": "https://blog.congty.vn/feed.xml",
          "Region": "Vietnam",
          "Topics": ["Programming"]
        }
      ]
    }
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
