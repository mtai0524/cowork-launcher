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
│                     JsonWorkspaceStore JsonRunHistoryStore│
│                     ScheduleEvaluator  FileLogger        │
│  Configuration/     JsonConfigEditor  IniConfigEditor    │
│                     XmlConfigEditor   ConfigFileService  │
│                     ConfigFileScanner ProgramScanner     │
│  Models/            ManagedApp  ScheduleRule  …          │
│  Validation/        AppValidator                         │
└───────────────────────────┬──────────────────────────────┘
                            │
┌───────────────────────────▼──────────────────────────────┐
│  Hệ điều hành: tiến trình con · file config · %APPDATA%  │
└──────────────────────────────────────────────────────────┘
```

`Cowork.Core` **không tham chiếu WPF**. Đó là ràng buộc cố ý: nếu sau này muốn làm bản CLI hoặc
Windows Service, toàn bộ logic dùng lại được nguyên vẹn. Cũng nhờ vậy mà 118 test chạy trong 1 giây
mà không cần dựng cửa sổ nào.

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

## Bộ đọc-ghi cấu hình

Tất cả cài `IConfigEditor` với hai thao tác: `Parse` (text → bảng khoá-giá trị) và `ApplyChanges`
(text gốc + các giá trị đã sửa → text mới).

Điểm mấu chốt: **`ApplyChanges` nhận text gốc, không nhận bảng.** Nó chỉ vá đúng những chỗ thay đổi.
Nếu ghi lại từ bảng, mọi comment và định dạng sẽ bay sạch.

| Editor | Cách làm phẳng | Cách ghi lại | Giữ được gì |
|---|---|---|---|
| `JsonConfigEditor` | `logging.level`, `servers[0].host` | Parse lại thành `JsonNode`, gán node lá, serialize | Kiểu dữ liệu (số vẫn là số, bool vẫn là bool) |
| `IniConfigEditor` | `[section]:key` | Thay đúng đoạn ký tự của giá trị **trên đúng dòng đó** | Comment, dòng trắng, khoảng trắng quanh `=`, kiểu nháy, CRLF/LF |
| `XmlConfigEditor` | `configuration/appSettings/add[2]/@value` | Nạp `XDocument` với `PreserveWhitespace`, gán rồi xuất | Comment, thụt lề, khai báo XML |
| `PlainTextConfigEditor` | — | Ghi thẳng text | Tất cả (không phân tích) |

`IniConfigEditor` sửa các dòng theo **thứ tự từ dưới lên** để chỉ số cột của những dòng phía trên
không bị lệch sau mỗi lần thay.

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

## Quản lý tiến trình

`ProcessManager` giữ một `ConcurrentDictionary<Guid, RunningApp>` — mỗi app tối đa một tiến trình
đang theo dõi.

**Hai chế độ loại trừ nhau.** Chạy quyền admin bắt buộc `UseShellExecute = true`, mà ShellExecute thì
không chuyển hướng được stdout. Nên bật "chạy quyền admin" đồng nghĩa mất nhật ký output —
`AppValidator` cảnh báo rõ điều này thay vì để người dùng tự thắc mắc sao log trống.

**Dừng lịch sự trước, kill sau.** `StopAsync` gọi `CloseMainWindow()` để app có cơ hội lưu dữ liệu,
chờ tối đa 5 giây, rồi mới `Kill(entireProcessTree: true)`. Diệt cả cây tiến trình là bắt buộc: một
file `.bat` thường sinh tiến trình con, kill mỗi `cmd.exe` sẽ để lại tiến trình mồ côi.

**Output có trần.** Mỗi app giữ một hàng đợi vòng (mặc định 2000 dòng) trong bộ nhớ; toàn bộ output
vẫn được nối vào file log riêng theo ngày. Một app chạy cả ngày in log liên tục không làm phình RAM.

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
