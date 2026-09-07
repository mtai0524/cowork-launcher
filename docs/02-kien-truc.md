# 02 — Kiến trúc

## Ba tầng

```
┌──────────────────────────────────────────────────────────┐
│  Cowork.App  (WPF, net8.0-windows)                       │
│  MainWindow.xaml ──binding──> MainViewModel              │
│                               ├─ AppViewModel            │
│                               ├─ ConfigFileViewModel     │
│                               ├─ ConfigEntryViewModel    │
│                               ├─ ScanConfigViewModel     │
│                               └─ ScanProgramViewModel    │
└───────────────────────────┬──────────────────────────────┘
                            │ chỉ gọi xuống, không có chiều ngược
┌───────────────────────────▼──────────────────────────────┐
│  Cowork.Core  (net8.0, không tham chiếu WPF)             │
│                                                          │
│  Services/          ProcessManager    DailyScheduler     │
│                     KeepAliveSupervisor KeepAlivePolicy   │
│                     HealthMonitor     HealthPolicy       │
│                     RunQueue          DependencyGraph    │
│                     SystemTriggerSupervisor               │
│                     JsonWorkspaceStore JsonRunHistoryStore│
│                     ScheduleEvaluator  FileLogger        │
│  Configuration/     JsonConfigEditor  IniConfigEditor    │
│                     XmlConfigEditor   ConfigFileService  │
│                     ConfigFileScanner ProgramScanner     │
│  Localization/      Loc  StringsVi  StringsEn            │
│  Models/            ManagedApp  ScheduleRule  …          │
│  Validation/        AppValidator                         │
└───────────────────────────┬──────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────┐
│  Hệ điều hành: tiến trình con · file config · %APPDATA%  │
└──────────────────────────────────────────────────────────┘
```

`Cowork.Core` **không tham chiếu WPF**. Đó là ràng buộc cố ý: nếu sau này muốn làm bản CLI hoặc
Windows Service, toàn bộ logic dùng lại được nguyên vẹn. Cũng nhờ vậy mà toàn bộ test chạy trong
vài giây mà không cần dựng cửa sổ nào.

## Composition root

Không dùng DI container. `App.OnStartup` lắp ráp tay theo đúng thứ tự phụ thuộc:

```csharp
var paths     = new CoworkPaths();                        // %APPDATA%\Cowork
var logger    = new FileLogger(paths);
var store     = new JsonWorkspaceStore(paths, logger);
var processes = new ProcessManager(logger, paths, settings.OutputBufferLines);
var history   = new JsonRunHistoryStore(paths, logger);
var configs   = new ConfigFileService();

var main = new MainViewModel(store, processes, history, configs, logger, paths, Dispatcher);
```

Với 6 phụ thuộc và một điểm khởi tạo, container chỉ thêm một lớp gián tiếp mà không giải quyết vấn
đề nào. Mọi service đều có interface, nên test vẫn thay thế được tự do.

## Luồng "chạy một app"

```
Người dùng bấm ▶            Scheduler tick (15s)
        │                            │
        │                   ScheduleEvaluator.IsDue(app, now)
        │                            │ true
        │                   MarkScheduled(app.Id, now)   ← đánh dấu TRƯỚC khi chạy
        │                            │
        └──────────┬─────────────────┘
                   ▼
        MainViewModel.RunApp(app, trigger)
                   │
                   ├─ AppValidator.Validate  ──lỗi──> hiện thông báo, dừng
                   │
                   ▼
        ProcessManager.Start(model, trigger)
                   │
                   ├─ dựng ProcessStartInfo (args, cwd, env, window style)
                   ├─ Process.Start
                   ├─ BeginOutputReadLine / BeginErrorReadLine
                   └─ hẹn giờ timeout nếu TimeoutMinutes > 0
                   │
        ┌──────────┴───────────────────────────────┐
        ▼                                          ▼
  OutputReceived (mỗi dòng)                  Exited (khi kết thúc)
        │                                          │
  Dispatcher.BeginInvoke                    Dispatcher.Invoke
        │                                          │
  AppViewModel.OutputLines.Add          history.Add(record) + cập nhật trạng thái
```

Điểm đáng lưu ý: `MarkScheduled` được gọi **trước** khi khởi chạy. Nếu việc khởi chạy ném lỗi, mốc
giờ đó vẫn coi như đã tiêu thụ — nếu không, tick sau (15 giây) sẽ thấy vẫn tới hạn và thử lại vô
hạn.

## Bộ lập lịch

`DailyScheduler` là một `System.Threading.Timer` tick mỗi **15 giây**. Mỗi tick nó hỏi
`ScheduleEvaluator` cho từng app, không tự tính toán gì.

Tách như vậy vì logic lịch là phần dễ sai nhất (múi giờ, đổi ngày, chạy bù, ngày trong tuần) và cũng
là phần khó test nhất nếu dính vào timer thật. `ScheduleEvaluator.IsDue(app, now)` là hàm thuần
nhận `now` làm tham số → test được mọi tình huống tức thì.

### Quy tắc tới hạn

**Lịch theo mốc giờ (`DailyAtTimes`)**

```
occurrence = mốc gần nhất đã qua tính tới now   (quét ngược tối đa 8 ngày, lọc theo ngày trong tuần)
nếu không có occurrence               → không tới hạn
nếu LastScheduledRunAt >= occurrence  → đã chạy mốc này rồi, bỏ qua
nếu CatchUpMissedRun                  → TỚI HẠN (chạy bù)
ngược lại                             → tới hạn chỉ khi (now - occurrence) <= 10 phút
```

Dung sai 10 phút để tick trễ hoặc máy vừa thức dậy vẫn kịp chạy, nhưng mở Cowork lúc 11h thì không
bất ngờ chạy lại mốc 7:30 sáng — trừ khi bạn chủ động bật "chạy bù".

**Lịch theo chu kỳ (`Interval`)**

```
sai ngày trong tuần         → không tới hạn
ngoài cửa sổ [Start, End]   → không tới hạn
chưa từng chạy              → TỚI HẠN ngay tick đầu tiên nằm trong cửa sổ
ngược lại                   → tới hạn khi (now - LastScheduledRunAt) >= Interval
```

**Lịch khi mở Cowork (`OnCoworkStartup`)** không do timer xử lý — `RunStartupApps()` được gọi một
lần lúc khởi động.

## Giữ app luôn chạy

`KeepAliveSupervisor` nghe `IProcessManager.RunCompleted`, hỏi `KeepAlivePolicy` rồi hẹn giờ.
Nó **không tự khởi chạy tiến trình** — cùng nguyên tắc với `DailyScheduler`: chỉ phát `RestartDue`
và để `MainViewModel.RunApp` lo, nhờ vậy mọi lần chạy (tay, lịch, khởi động lại) đều đi qua cùng một
chỗ kiểm tra cấu hình và ghi lịch sử.

```
Process.Exited ──> ProcessManager.RunCompleted
                          │
                          ├─> MainViewModel.OnRunCompleted     (lịch sử, thông báo lỗi)
                          │
                          └─> KeepAliveSupervisor.OnRunCompleted
                                  │  KeepAlivePolicy.Decide(app, record, cácLầnTrước, now)
                                  ├─ StoppedByUser / NeverStarted / NotKeepAlive ──> thôi
                                  ├─ LimitReached ──> GaveUp ──> trạng thái Failed + thông báo
                                  └─ Restart ──> Timer(delay) ──> RestartDue ──> RunApp(KeepAlive)
```

`KeepAlivePolicy.Decide` là hàm thuần nhận `now`, giữ đúng ràng buộc số 5 — nhờ vậy cửa sổ đếm
"tối đa N lần mỗi giờ" kiểm thử được mà không phải chờ một giờ thật.

Ba quyết định đáng chú ý:

- **Dừng tay thì không khởi động lại.** `RunOutcome.Cancelled` chỉ sinh ra từ nút Dừng/Dừng tất cả;
  đó là ý người dùng.
- **Chưa từng chạy được thì không thử lại.** Thiếu file hay bị từ chối quyền (`NotStarted`) chạy lại
  cũng thế — khởi động lại chỉ tạo vòng lặp vô nghĩa.
- **Timeout vẫn được khởi động lại.** Với keep-alive, chỉ có "app còn chạy hay không" là quan trọng,
  nên *timeout + keep-alive* trở thành cách tự khởi động lại định kỳ.

Supervisor đọc lại app **tại thời điểm tới giờ** chứ không giữ bản chụp lúc app thoát: người dùng có
thể vừa tắt keep-alive hoặc tắt app trong lúc đếm ngược.

`RetrySupervisor` đi cùng khuôn nhưng cho job chạy xong là thoát: `RetryPolicy.Decide` nhận số lần đã
thử trong *chuỗi* hiện tại thay vì cửa sổ một giờ, chỉ thử lại khi kết quả là `Failed` hay `TimedOut`,
và từ chối app đang bật keep-alive để hai cơ chế không cùng khởi chạy một app. Lần chạy có nguồn khác
`Retry` luôn mở chuỗi mới, nên bấm Chạy tay là đếm lại từ đầu. Hết lượt thì phát `RetriesExhausted`;
`MainViewModel` chỉ thông báo ở khay tại đó, còn các lần lỗi giữa chuỗi thì im lặng — thứ mà thử lại
sinh ra để xử lý thì không cần đánh thức ai.

`MainViewModel` có thêm một lưới lọc nhỏ: sau khi khởi động lại nhanh, tiến trình *cũ* có thể báo
"đã thoát" **sau** khi tiến trình mới đã chạy; `IsStaleExit` so PID để không hiện Idle trong khi app
đang chạy.

## Chạy theo sự kiện của máy

Lịch dựa trên đồng hồ bỏ lỡ đúng những lúc đáng chạy nhất: máy vừa ngủ dậy, mạng vừa có lại.
`SystemTriggerSupervisor` bổ sung ba mốc đó, độc lập với `ScheduleRule` để một app dùng được cả hai.

```
Windows ──> WindowsSystemEventSource        (Cowork.App — SystemEvents, NetworkChange)
                     │ ISystemEventSource.Occurred
                     ▼
            SystemTriggerSupervisor          (Cowork.Core)
                     │ SystemTriggerPolicy.Decide(app, kind, lầnTrước, đangChạy, now)
                     ├─ NotSubscribed / CannotRun / AlreadyRunning / TooSoon ──> thôi
                     └─ chạy ──> Timer(SystemTriggerDelaySeconds) ──> Due ──> RunApp(SystemEvent)
```

Ba điểm đáng biết:

- **Phần lắng nghe nằm ở tầng ứng dụng.** `SystemEvents` là API Windows, còn `Cowork.Core` phải chạy
  được ở nơi khác — nên Core chỉ giữ interface `ISystemEventSource` và bản giả
  `NullSystemEventSource` (cũng là thứ test dùng để bắn sự kiện theo ý mình).
- **Khoảng lặng một phút cho mỗi cặp app–sự kiện.** Windows bắn `Resume` nhiều lần cho một lần thức
  dậy, và mạng chập chờn làm `NetworkAvailable` nảy liên tục. Mốc khoảng lặng được ghi **lúc nhận sự
  kiện**, không phải lúc chạy — nếu ghi lúc chạy thì cả loạt sự kiện dồn trong khoảng chờ sẽ lọt hết.
- **Độ trễ mặc định 15 giây.** Ngay sau khi thức dậy, card mạng và dịch vụ hệ thống chưa sẵn sàng;
  chạy ngay thường lỗi. Như mọi supervisor khác, app được đọc lại tại thời điểm tới giờ.

## Phụ thuộc giữa các app

"Chạy tất cả" trước đây bung mọi app cùng lúc theo thứ tự danh sách. `RunQueue` giữ nguyên hành vi
đó cho app không khai gì, và chờ đúng thứ tự cho app có khai.

```
RunAll ──> RunQueue.Start(batch)
              │
              ├─ DependencyGraph.TopologicalOrder ──> app trong vòng lặp: bỏ qua ngay
              │
              └─ Pump()  ◄──────────────────┐
                   │                         │ StatusChanged(Running) · RunCompleted
                   ├─ đủ phụ thuộc ──> runner(app) ──> MainViewModel.RunApp
                   ├─ chắc chắn không đủ ──> Skipped
                   └─ cả lượt đứng yên ──> Finished
```

Bốn điểm đáng biết:

- **Hàng đợi nhận một `runner` thay vì phát sự kiện**, khác mọi supervisor khác. Lý do: nó cần
  *biết kết quả* của lệnh khởi chạy. Cấu hình lỗi thì `RunApp` trả false mà không có `RunCompleted`
  nào tới — không biết điều đó thì các app phía sau chờ mãi. Tầng ứng dụng vẫn là nơi quyết định
  chạy thế nào; hàng đợi chỉ hỏi "chạy được không".
- **`Pump` chạy ngoài khoá và lặp cho tới khi đứng yên.** `ProcessManager` phát "đang chạy" *ngay
  trong* lệnh khởi chạy, nên `runner` có thể gọi ngược vào hàng đợi khi nó còn đang xét — cờ
  `_pumping`/`_pumpAgain` gộp lần gọi lồng đó vào vòng sau. Mỗi vòng có động tĩnh (khởi chạy hoặc bỏ
  qua) đều kéo theo một vòng nữa, vì bỏ qua một app làm những app chờ nó cũng phải bỏ.
- **Chờ hết chuỗi thử lại rồi mới kết luận.** Một lần chạy lỗi nhưng app có đặt *Thử lại* thì
  `RetrySupervisor` sẽ chạy lại; hàng đợi đếm số lần chạy nguồn `Retry` và chỉ coi là hỏng khi đã
  hết lượt. Không thế thì các app phía sau bị bỏ oan trong khi lần thử lại sắp thành công.
- **App đang chạy sẵn không bị khởi chạy lại.** *Không chạy chồng* sẽ chặn lần thứ hai; hàng đợi
  nhận ra và chờ đúng instance đang sống.

`DependencyGraph` giữ toàn bộ phần thứ tự dưới dạng hàm thuần — sắp xếp Kahn, dò vòng lặp, và
kiểm tra những khai báo không bao giờ thoả được (trỏ tới app đã xoá, chờ "xong việc" ở app giữ luôn
chạy). Nhờ vậy phần dễ sai nhất kiểm thử được mà không cần tiến trình nào.

Phụ thuộc **chỉ áp dụng cho "Chạy tất cả"**. Bấm ▶ Chạy cho một app là ý người dùng muốn đúng app
đó; lịch cũng kích hoạt từng app riêng lẻ.

## Kiểm tra sức khoẻ

Keep-alive chỉ thấy tiến trình *thoát*; app còn sống mà đơ thì nó bó tay. `HealthMonitor` bịt chỗ
đó bằng ba dấu hiệu, tất cả cùng dẫn tới một kết cục: dừng app với `RunOutcome.Unhealthy`.

```
StatusChanged(Running) ──> HealthMonitor bắt đầu theo dõi (nếu HealthPolicy.AppliesTo)
                                  │
        ┌─────────────────────────┼─────────────────────────┐
        ▼                         ▼                         ▼
  Timer mỗi IntervalSeconds   OutputReceived            RunCompleted
        │                         │                         │
  HealthPolicy.OnTick        khớp FailurePatterns?     gỡ theo dõi (đúng PID)
        │                         │
        ├─ Wait (còn ân hạn)      └─ có ──┐
        ├─ Terminate (im lặng) ───────────┤
        └─ Probe ──> IHealthProbe         │
                       │ lỗi liên tiếp ≥ FailureThreshold
                       └─────────────────┬┘
                                         ▼
                    ProcessManager.TerminateAsync(Unhealthy, lý do)
                                         │
                                  RunCompleted(Unhealthy)
                                         │
                          KeepAliveSupervisor / RetrySupervisor
```

Bốn quyết định đáng chú ý:

- **Monitor dừng app trực tiếp**, khác `DailyScheduler` và hai supervisor vốn chỉ phát sự kiện. Dừng
  không cần kiểm tra cấu hình hay ghi lịch sử riêng: `ProcessManager.TerminateAsync` ghi kết quả và
  lý do vào đúng bản ghi của lần chạy đó. Còn phần *chạy lại* vẫn đi qua keep-alive / thử lại như
  mọi lần kết thúc khác — treo với crash từ đó về sau không khác gì nhau.
- **`HealthPolicy` là hàm thuần nhận `now`**, giữ đúng ràng buộc số 5: thời gian ân hạn và ngưỡng im
  lặng kiểm thử được bằng `FixedClock` mà không phải chờ phút thật.
- **Bộ đếm lỗi chỉ tính chuỗi liên tiếp.** Một lần thăm dò thành công đặt lại về 0; mạng chập một
  nhịp không giết một app đang khoẻ.
- **Cấu hình được chụp lại mỗi nhịp** (`Snapshot`), vì luồng đọc output không thể hỏi `IAppSource`
  cho từng dòng — đó thường là view-model, phải nhảy lên luồng giao diện. Bộ regex đã biên dịch được
  giữ lại khi danh sách mẫu không đổi.

`HealthMonitor` cũng so PID khi gỡ theo dõi, cùng lý do với `IsStaleExit` ở `MainViewModel`: sau một
lần khởi động lại nhanh, tiến trình cũ có thể báo kết thúc sau khi tiến trình mới đã được theo dõi.

## Thông báo

`MainViewModel` chỉ phát `NotificationRaised(title, message, severity)`; `MainWindow` hiện bong bóng
ở khay qua `TaskbarIcon.ShowBalloonTip` và mở lại cửa sổ khi bấm vào. Không dùng Windows toast API
vì nó kéo theo package và đăng ký AUMID, trong khi bong bóng khay có sẵn từ thư viện đã dùng.

Chạy tay thì không thông báo — người dùng đang nhìn thanh trạng thái. Thông báo chỉ dành cho lúc
Cowork nằm dưới khay: lịch, khởi động, và mọi app bật keep-alive.

## Quản lý từ xa

Hai dự án thêm vào, cố ý tách để phần quyết định vẫn test được như Core:

```
Cowork.App ──┐
             ├──▶ Cowork.Remote  (hợp đồng dữ liệu, MachineRegistry, AgentDirectory, HubClient)
Cowork.Hub ──┘         │
                       └──▶ Cowork.Core  (model, ScheduleEvaluator, Loc)
```

- **`Cowork.Remote`** không dính WPF lẫn ASP.NET. `MachineRegistry` là sổ máy thuần trạng thái,
  `AgentDirectory` tra token → tên máy, `HubClient` là client SignalR nói chuyện qua giao diện
  `IAgentHost` — nên test tích hợp có thể nối một agent giả vào hub thật.
- **`Cowork.Hub`** là lớp mỏng: `AgentHub` xác thực token lúc nối rồi chuyển mọi thứ cho registry;
  giao diện Blazor Server chỉ đọc registry và gọi `SendAsync`.
- **`MainViewModel` cài `IAgentHost`**: hai phương thức của nó bị gọi từ luồng mạng nên đều nhảy về
  `Dispatcher` — đúng ràng buộc số 2. Lệnh từ xa đi qua cùng `RunApp` với mọi nguồn khác, ghi lịch
  sử với nguồn `Remote`.

Hai chọn lựa đáng biết: agent gọi **ra** hub (máy sau NAT không nhận kết nối vào được), và hub
**không lưu gì** — agent là nguồn sự thật, hub chỉ phản ánh. `Loc` được thêm bản nhận ngôn ngữ tường
minh `Loc.T(language, key)` vì server phục vụ nhiều người thì biến tĩnh `Loc.Current` là sai.

Chi tiết cài đặt, bảo mật và giới hạn: [06-quan-ly-tu-xa.md](06-quan-ly-tu-xa.md).

## Bộ đọc-ghi cấu hình

Tất cả cài `IConfigEditor` với hai thao tác: `Parse` (text → bảng khoá-giá trị) và `ApplyChanges`
(text gốc + các giá trị đã sửa → text mới).

Điểm mấu chốt: **`ApplyChanges` nhận text gốc, không nhận bảng.** Nó chỉ vá đúng những chỗ thay đổi.
Nếu ghi lại từ bảng, mọi comment và định dạng sẽ bay sạch.

| Editor | Cách làm phẳng | Cách ghi lại | Giữ được gì |
|---|---|---|---|
| `JsonConfigEditor` | `logging.level`, `servers[0].host` | Dò khoảng byte của từng giá trị bằng `Utf8JsonReader`, thay đúng khoảng đó | Comment JSONC, thụt lề, dòng trắng, escape sẵn có, kiểu dữ liệu |
| `IniConfigEditor` | `[section]:key` | Thay đúng đoạn ký tự của giá trị **trên đúng dòng đó** | Comment, dòng trắng, khoảng trắng quanh `=`, kiểu nháy, CRLF/LF |
| `XmlConfigEditor` | `configuration/appSettings/add[2]/@value` | Nạp `XDocument` với `PreserveWhitespace`, gán rồi xuất | Comment, thụt lề, khai báo XML |
| `PlainTextConfigEditor` | — | Ghi thẳng text | Tất cả (không phân tích) |

`IniConfigEditor` sửa các dòng theo **thứ tự từ dưới lên** để chỉ số cột của những dòng phía trên
không bị lệch sau mỗi lần thay. `JsonConfigEditor` cũng vá từ cuối file ngược lên, vì lý do y hệt.

#### Hai kiểu chú thích trong JSON

JSON chuẩn không có cú pháp comment, nên file cấu hình ngoài đời né bằng hai cách, Cowork đọc được cả hai:

```jsonc
{
  // comment JSONC — giữ nguyên khi lưu, vì ApplyChanges vá trên text gốc
  "//api_key": "Groq API key ...",   // khoá giả: gộp vào cột Ghi chú của "api_key"
  "api_key": "gsk_..."
}
```

Khoá `"//ten"` chỉ được gộp vào ghi chú khi `"ten"` **có thật và là giá trị đơn**. Hai trường hợp
còn lại giữ nguyên thành dòng riêng, cố ý:

- `"//lang"` mà file không có `"lang"` — đó là mô tả một mặc định chưa bật; giấu đi là người dùng
  mất luôn nội dung đang nằm trong file.
- `"//server"` mà `"server"` là một nhánh con — chú thích không biết bám vào dòng nào.

### Dò tìm file cấu hình

`ConfigFileScanner` trả lời câu hỏi "file nào trong thư mục này là cấu hình" bằng ba tầng lọc, cố ý
xếp rẻ trước đắt sau:

1. **Nhận ứng viên** — đúng một trong bốn dạng: có phần mở rộng quen thuộc
   (`.json .ini .env .xml .config .yaml .toml`…); là `.env*`; là file kiểu rc (`.npmrc`); **hoặc
   không có đuôi mà tên là `config`/`settings`/`conf`…**. Loại ngay: thư mục nhiễu, file lock,
   `*.min.*`, file > 2 MB, và file mở đầu bằng `#!` (script chứ không phải cấu hình).
2. **Chấm điểm theo tên và ngữ cảnh** — `appsettings*.json`, `web.config`, `.env`, file không đuôi
   tên `config` → **Cao**; đuôi `.ini/.env/.conf` hoặc nằm trong thư mục `config/` → **Vừa**; còn
   lại → **Thấp**. Manifest dự án (`package.json`, `tsconfig.json`) và file mẫu (`.env.example`) bị
   hạ bậc có chủ ý.
3. **Thử phân tích thật** — file JSON/XML không parse được thì hạ một bậc và gắn cờ, chứ không loại
   hẳn (có thể chính nó là file bạn cần vào sửa).

**File không có phần mở rộng.** Đây không phải trường hợp hiếm — `~/.config/<app>/config`,
`~/.ssh/config`, `~/.aws/config` đều thế. Hai hệ quả:

- Thư mục bắt đầu bằng dấu chấm **không được** bỏ qua hàng loạt: `.config` và `.ssh` chính là nơi
  hay chứa cấu hình nhất. Chỉ những thư mục công cụ cụ thể (`.git`, `.vs`, `.venv`, `.next`…) mới
  nằm trong danh sách loại.
- Định dạng phải đoán từ **nội dung**: `{` → JSON, `<` → XML, có dòng `khoá = giá trị` hoặc
  `[section]` → INI, còn lại → văn bản thuần. Dấu `[` được phân biệt bằng hình dạng dòng — `[db]`
  là section INI, `[ {...} ]` là mảng JSON.

Việc đoán nội dung **chỉ áp dụng cho file không đuôi**. File `.yaml`/`.toml` đã biết rõ là gì rồi;
đoán thêm chỉ tổ nhận nhầm — một workflow YAML có dòng `if: github.ref == '...'` sẽ bị tưởng là INI
vì có dấu `=`.

Khi không nhận ra hình dạng nào (ví dụ `~/.ssh/config` dùng cú pháp `Khoá giá-trị` không có dấu
`=`), Cowork trả về văn bản thuần và mở ở chế độ sửa nguồn — nói thẳng "định dạng này chỉ sửa nguồn
được" tốt hơn là hiện một bảng rỗng khó hiểu.

Hậu tố mẫu được bóc trước khi đọc phần mở rộng: `config.example` được xét như `config`,
`appsettings.json.sample` như `appsettings.json` — chỉ khác là điểm tin cậy bị hạ xuống Vừa.

Kết quả luôn là *gợi ý*: chỉ mức Cao được tick sẵn, và không file nào được thêm khi người dùng chưa
xác nhận. Đoán sai ở đây dẫn tới ghi đè nhầm file, nên chi phí của một cú tick thừa rẻ hơn nhiều so
với chi phí của một lần đoán sai.

### Dò tìm chương trình

`ProgramScanner` trả lời câu hỏi song song: "file nào trong thư mục này chạy được". Chỉ nhận
`.exe`, `.com`, `.bat`, `.cmd`, `.ps1` — đúng những gì `ProcessManager` khởi chạy được.

Điểm dễ sai nhất khi làm bộ quét thứ hai này là **copy nguyên danh sách thư mục nhiễu từ bộ quét
cấu hình**. Hai danh sách gần như ngược nhau:

| Thư mục | Quét cấu hình | Quét chương trình |
|---|---|---|
| `bin`, `dist`, `build`, `publish` | rác — bỏ qua | **chính là nơi file .exe nằm** |
| `obj`, `node_modules`, `.git`, `.vs` | bỏ qua | bỏ qua |
| `.config`, `.ssh` | nơi quan trọng nhất | không liên quan |

Chấm điểm: tên file trùng tên thư mục gốc (`gcm\gcm.exe`) hoặc là từ khoá khởi chạy
(`run`, `start`, `main`, `chay`…) → **Cao**; nằm ngay thư mục gốc hoặc trong thư mục build →
**Vừa**; nằm sâu → **Thấp**. Trình cài đặt (`unins*`, `setup*`, `vcredist*`) bị hạ xuống Thấp;
tàn dư build (`*.vshost.exe`, `crashpad_handler.exe`) bị loại hẳn.

`ProgramScanner.BuildCommand` dựng lệnh cuối cùng. File `.ps1` **không khởi chạy trực tiếp được**
nên được bọc thành `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<đường dẫn>"`. Nếu
thiếu bước này, bộ quét sẽ gợi ý ra những app lỗi ngay lần chạy đầu — gợi ý sai còn tệ hơn không
gợi ý.

`ConfigFileService` bọc bên ngoài và lo phần I/O: nhận diện BOM để ghi lại đúng encoding cũ (quan
trọng với file có tiếng Việt), tạo `.cowork.bak`, và ghi qua file tạm rồi `File.Replace` để mất điện
giữa chừng không làm hỏng config.

### Phát hiện sửa đồng thời

Mỗi lần đọc, `ConfigFileService` gắn vào tài liệu một `FileStamp` (giờ ghi cuối + kích thước). Dấu
được chụp **trước** khi đọc nội dung: chụp sau thì một lần ghi xen giữa lúc đọc sẽ lọt lưới.

`ConfigFileViewModel.Save` từ chối ghi khi dấu hiện tại khác dấu đã chụp, và nhịp 30 giây của giao
diện gọi `CheckForExternalChange` cho đúng file đang mở để dải cảnh báo hiện lên trước cả khi người
dùng bấm Lưu. Chỉ file đang mở mới bị soi — đủ để bịt lỗ ghi đè mà không phải dựng `FileSystemWatcher`
cho từng file của từng app.

So metadata thay vì băm nội dung là đánh đổi có chủ ý: rẻ để chạy định kỳ, và bắt hụt trường hợp
"sửa mà giữ nguyên cả giờ ghi lẫn kích thước" — vốn chỉ xảy ra khi ai đó cố tình.

### So sánh và khôi phục

`LineDiff` là hàm thuần, không đụng đĩa, nên dùng lại được cho mọi cặp text: bản sắp ghi với bản
trên đĩa, bản `.cowork.bak` với bản hiện tại. Cách làm là cắt phần đầu và phần đuôi giống nhau
trước rồi mới chạy LCS trên khúc giữa — với file cấu hình (sửa vài dòng giữa hàng nghìn dòng) bước
cắt này giải quyết gần hết, nên bảng quy hoạch động hiếm khi lớn. Khúc giữa vượt `MaxBlockLines`
thì trả về "bỏ hết bên trái, thêm hết bên phải" thay vì dựng bảng hàng trăm triệu ô.

`RestoreBackup` **tráo** file và bản sao lưu cho nhau thay vì chỉ chép một chiều: khôi phục nhầm
thì bấm lần nữa là về chỗ cũ, và không có thao tác nào làm mất hẳn một trong hai nội dung.

## Quản lý tiến trình

`ProcessManager` giữ một `ConcurrentDictionary<Guid, RunningApp>` — mỗi app tối đa một tiến trình
đang theo dõi.

**Hai chế độ loại trừ nhau.** Chạy quyền admin bắt buộc `UseShellExecute = true`, mà ShellExecute thì
không chuyển hướng được stdout. Nên bật "chạy quyền admin" đồng nghĩa mất nhật ký output —
`AppValidator` cảnh báo rõ điều này thay vì để người dùng tự thắc mắc sao log trống.

**Dừng lịch sự trước, kill sau.** `StopAsync` thử `CloseMainWindow()` để app GUI có cơ hội lưu dữ liệu;
app console không có cửa sổ chính thì `ConsoleSignal` tạm nối Cowork vào console của nó và phát Ctrl+C —
tín hiệu đi tới cả cây tiến trình dùng chung console, nên server thật bên trong một file `.bat` cũng
nhận được. Sau `StopGraceSeconds` của app (mặc định 5 giây) mới `Kill(entireProcessTree: true)`. Diệt
cả cây là bắt buộc: một file `.bat` thường sinh tiến trình con, kill mỗi `cmd.exe` sẽ để lại tiến trình
mồ côi. Cowork cũng nhận đúng Ctrl+C mà nó vừa phát, và handler mặc định của Windows là tắt tiến trình.
Cài handler riêng không ăn thua vì `AttachConsole`/`FreeConsole` xoá sạch danh sách handler; thứ sống
sót là cờ "bỏ qua Ctrl+C" của tiến trình, nên `ConsoleSignal` bật cờ đó trước khi phát. Cờ này lại được
tiến trình con kế thừa — con sinh ra lúc cờ đang bật sẽ điếc với Ctrl+C mãi — nên mọi tiến trình con
phải khởi chạy qua `ConsoleSignal.StartChild`, nơi cờ được tắt ngay trước `Process.Start`, dưới cùng
một khoá với việc gửi tín hiệu.

**Mã thoát thành công do app quyết định.** `RunningApp` chụp `SuccessExitCodes` lúc khởi chạy; mã thoát
nằm trong danh sách ⇒ `Succeeded`, còn lại ⇒ `Failed`. Mặc định chỉ có 0, nhưng robocopy trả 1 khi đã
sao chép và trình cài đặt trả 3010 khi cần khởi động lại máy — coi chúng là lỗi thì lịch sử sai và khay
báo lỗi giả, kéo theo cả thử lại vô ích.

**Output có trần.** Mỗi app giữ một hàng đợi vòng (mặc định 2000 dòng) trong bộ nhớ; toàn bộ output
vẫn được nối vào file log riêng theo ngày. Một app chạy cả ngày in log liên tục không làm phình RAM.
Trên đĩa thì `LogPruner` xoá file cũ hơn `LogRetentionDays` lúc mở Cowork và vào đầu mỗi ngày; phần
chọn file nhận `today` làm tham số và chỉ đụng đúng hai mẫu tên Cowork tự sinh.

## Luồng dữ liệu giữa view-model và model

`AppViewModel` **ghi thẳng vào `ManagedApp`** ngay khi người dùng gõ, qua các hàm `OnXxxChanged`
sinh bởi `[ObservableProperty]`:

```csharp
partial void OnNameChanged(string value) => Sync(() => Model.Name = value);
```

Nhờ đó không cần bước "áp dụng thay đổi" riêng — lưu workspace chỉ là serialize danh sách model.
Cờ `_suspendSync` chặn ghi ngược trong lúc constructor đang nạp giá trị ban đầu.

`MainViewModel` lắng nghe `PropertyChanged` của mọi `AppViewModel` để bật cờ "có thay đổi chưa lưu",
nhưng **bỏ qua các thuộc tính chỉ phục vụ hiển thị** (`RuntimeState`, `StatusText`, `NextRunText`…) —
nếu không, một app đang chạy sẽ liên tục báo "chưa lưu" dù người dùng chẳng sửa gì.

## Luồng (threading)

| Chạy trên luồng nào | Cái gì |
|---|---|
| UI thread | Toàn bộ view-model, `ObservableCollection` |
| Thread pool | `DailyScheduler.Tick`, `Process.Exited`, `OutputDataReceived` |

Mọi sự kiện từ tầng dưới đều đi qua `Dispatcher` trước khi chạm vào view-model:

- `OutputReceived` → `Dispatcher.BeginInvoke` (không chặn, vì output có thể rất dày)
- `StatusChanged`, `RunCompleted`, `AppDue` → `Dispatcher.Invoke` (cần thứ tự đúng)

`DailyScheduler.Tick` có cờ `_ticking`: một tick chạy lâu (do đang khởi chạy app) sẽ không bị tick
sau chồng lên.

## Đa ngôn ngữ

Bảng chuỗi nằm ở `Cowork.Core/Localization` dưới dạng dictionary trong mã nguồn, không dùng `.resx`.
Hai lý do: tầng Core không được kéo theo hạ tầng WPF, và tra cứu ngay lúc gọi cho phép đổi ngôn ngữ
**không cần khởi động lại**.

```
Loc.Current = AppLanguage.English
      │
      ├─ Loc.LanguageChanged ──> MainViewModel.RefreshLocalizedText()
      │                             ├─ LocalizedStrings.Instance.Refresh()   ← nhãn tĩnh trên XAML
      │                             ├─ AppViewModel.RefreshLocalizedText()   ← nhãn do VM sinh ra
      │                             └─ nạp lại History                       ← nhãn do converter sinh ra
      └─ workspace.Settings.Language
```

Có ba nhóm chuỗi, mỗi nhóm cập nhật theo một đường khác nhau:

| Nhóm | Ví dụ | Cách cập nhật |
|---|---|---|
| Nhãn tĩnh trên XAML | `{loc:Tr AppList.Title}` | binding tới `LocalizedStrings`, tự làm tươi |
| Chuỗi view-model tính sẵn | `StatusText`, `ScheduleSummary` | view-model bắn `PropertyChanged` |
| Nhãn do converter sinh | cột Kết quả trong bảng lịch sử | phải nạp lại `ItemsSource` |

Ràng buộc khi thêm chuỗi mới:

1. **Thêm khoá vào cả hai bảng.** `LocalizationTests` chặn lệch khoá, chuỗi rỗng, và lệch số chỗ
   chèn `{0}` giữa hai ngôn ngữ (lệch chỗ chèn là `FormatException` lúc chạy).
2. **Không so sánh chuỗi đã dịch** để quyết định logic. Chip mức tin cậy so theo enum
   `ScanConfidence`, không so theo nhãn `"Cao"` — nếu không, đổi sang tiếng Anh là mất màu.
3. **Danh sách trong ComboBox bọc qua `ChoiceViewModel<T>`.** Enum trần không có `PropertyChanged`
   nên nhãn không vẽ lại được; mà thay cả `ItemsSource` để ép vẽ lại thì ComboBox xoá mất lựa chọn.

## Đa chủ đề màu

`Themes/Palettes/*.xaml` chỉ chứa màu; `Themes/Theme.xaml` chỉ chứa kiểu dáng điều khiển.
`ThemeManager.Apply` tráo đúng phần tử **số 0** trong `MergedDictionaries` của `App`.

```
App.Resources.MergedDictionaries
  [0] Palettes/<Theme>.xaml   ← ThemeManager tráo ô này
  [1] Theme.xaml              ← tra màu bằng DynamicResource
```

Ràng buộc:

1. **Mọi tham chiếu màu phải là `DynamicResource`.** `StaticResource` nạp một lần lúc dựng cây giao
   diện, nên đổi chủ đề sẽ chỉ ăn một nửa. `ThemeContrastTests` đối chiếu mọi khoá `DynamicResource`
   trong markup với các bảng màu — WPF nuốt im lặng khoá gõ sai, không có lỗi nào báo trước.
2. **Không đặt mã màu thẳng trong XAML hay C#.** Màu theo trạng thái đi qua `DataTrigger` +
   `DynamicResource` (xem `StatusDot`, `OutcomeText` trong `MainWindow.xaml`), không qua converter
   trả về `Brush` — converter trả về một brush cố định, đổi chủ đề không cập nhật lại được.
3. **Mọi bảng màu phải khai đủ bộ khoá.** Bảng mới thêm vào `AppTheme` phải có file cùng tên trong
   `Themes/Palettes`, cùng danh sách khoá, và một `SolidColorBrush` cho mỗi `Color`.
4. **Chữ phải đọc được trên nền.** `ThemeContrastTests` tính tỉ lệ tương phản WCAG 2.1 cho từng cặp
   chữ/nền của cả bốn bảng màu. Chữ chính trên nền chính đặt ngưỡng 7:1, phần còn lại 4.5:1 — cao
   hơn mức tối thiểu của chuẩn, vì đây là phần mềm nhìn cả ngày.

## Chống mất dữ liệu

Mọi thao tác ghi đều theo cùng một khuôn: **ghi ra file tạm → `File.Replace`**. Áp dụng cho cả
`workspace.json` lẫn file cấu hình của app. Mất điện giữa chừng thì file cũ vẫn nguyên vẹn.

Ngoài ra `JsonWorkspaceStore` chụp lại `workspace.json` **một lần mỗi ngày** trước khi ghi đè, vào
`backups/workspace-YYYYMMDD.json`, giữ 10 bản gần nhất. Ghi nguyên tử chống hỏng file giữa chừng,
còn bản chụp theo ngày chống trường hợp nội dung bị thay đổi sai — kể cả do thứ khác ngoài Cowork
ghi đè lên.

Nếu `workspace.json` hỏng (sửa tay sai cú pháp), `JsonWorkspaceStore` đổi tên nó thành
`workspace.json.corrupt-<timestamp>` rồi khởi động với workspace rỗng — giữ bằng chứng để cứu dữ
liệu, thay vì ghi đè mất luôn hoặc chặn không cho mở app.
