# 05 — Giới hạn hiện tại và lộ trình

## Giới hạn đã biết

Những điều dưới đây là **hệ quả của thiết kế hiện tại**, không phải bug. Ghi lại để bạn biết trước
khi đụng phải.

### Lịch chỉ chạy khi Cowork đang mở

Bộ lập lịch là một timer trong tiến trình Cowork. Máy tắt hoặc chưa đăng nhập Windows thì không có
gì chạy.

Giảm nhẹ hiện có: bật *Khởi động cùng Windows* + *Thu nhỏ xuống khay*, và bật *Chạy bù* cho những
việc bắt buộc phải chạy trong ngày.

Giải pháp triệt để cần một Windows Service — xem [Hướng phát triển](#hướng-phát-triển).

### Chạy quyền admin thì mất nhật ký output

Đây là ràng buộc của Windows: nâng quyền bắt buộc dùng ShellExecute, mà ShellExecute không chuyển
hướng được stdout/stderr. `AppValidator` cảnh báo khi bạn bật cả hai. Cách vòng: để script tự ghi
log ra file, rồi khai file đó làm file cấu hình dạng văn bản để xem trong Cowork.

### Chưa có `TimeoutMinutes` cho tiến trình chạy quyền admin

`Process.Exited` vẫn bắt được, nhưng `Kill(entireProcessTree)` có thể thất bại do khác mức toàn vẹn
(integrity level). Timeout với app admin không đảm bảo.

### Dừng lịch sự với script `.bat`: cmd hỏi "Terminate batch job (Y/N)?"

Ctrl+C tới một file `.bat` khiến `cmd.exe` dừng lại hỏi câu trên và chờ bàn phím — không ai trả lời,
nên nó đứng đó tới hết thời gian ân hạn rồi bị kill. Chương trình thật mà script gọi vẫn nhận được
Ctrl+C và tự dọn dẹp bình thường; chỉ cái vỏ `cmd` là chết cứng. Muốn dừng nhanh, giảm *Chờ dừng lịch
sự tối đa* của app đó. App GUI chưa kịp lên cửa sổ và app chạy quyền admin không có console để nhận
Ctrl+C, nên bị kill ngay.

### Sửa đồng thời không được phát hiện

Nếu bạn mở một file cấu hình trong Cowork, rồi sửa file đó bằng công cụ khác, Cowork **không** biết
và sẽ ghi đè khi bạn bấm Lưu. Bấm **Tải lại** trước khi sửa nếu nghi ngờ. Bản `.cowork.bak` vẫn giữ
nội dung ngay trước lần ghi đè đó.

### Chế độ sửa nguồn không tô màu cú pháp

Là `TextBox` thuần. File JSON vài nghìn dòng sẽ khó nhìn — dùng chế độ bảng cho những file như vậy.

### JSON: chỉ sửa được giá trị có sẵn

Chế độ bảng cho phép **sửa** giá trị, không cho **thêm khoá mới** hay **xoá khoá**. Thêm/xoá khoá
phải làm ở chế độ sửa nguồn. Đây là lựa chọn có chủ ý: thêm khoá vào đúng vị trí trong cây JSON mà
vẫn giữ định dạng là bài toán khó hơn nhiều so với giá trị nó mang lại.

### Một app = một tiến trình

Cowork theo dõi tối đa một tiến trình cho mỗi app. Tắt `SingleInstance` thì vẫn khởi chạy được nhiều
lần, nhưng chỉ tiến trình mới nhất được theo dõi và dừng được từ giao diện.

### Thông báo chỉ là bong bóng ở khay

Cowork báo lỗi bằng bong bóng khay hệ thống, đủ cho người ngồi tại máy. Không có email, không có
webhook — app lỗi lúc 2h sáng thì sáng ra vẫn phải nhìn khay hoặc tab Lịch sử.

### Phụ thuộc chỉ áp dụng cho "Chạy tất cả"

Lịch kích hoạt từng app riêng lẻ, nên hai app cùng đặt lịch 02:00 vẫn chạy song song dù có khai phụ
thuộc. Cách vòng hiện có: đặt lịch cho app đầu chuỗi, còn lại để trong lượt "Chạy tất cả". Làm cho
lịch tôn trọng phụ thuộc cần bộ lập lịch đẩy cả chuỗi vào `RunQueue` thay vì phát từng `AppDue`.

### Kiểm tra sức khoẻ không thay được giám sát thật

*Kiểm tra sức khoẻ* bắt được app đơ qua thăm dò cổng/URL và watchdog theo output, nhưng nó chạy
trong tiến trình Cowork: Cowork tắt thì không ai thăm dò. Watchdog theo output cũng chỉ hoạt động
khi bật *Thu nhật ký output* và app không chạy quyền admin — Windows không cho chuyển hướng stdout
của tiến trình nâng quyền.

### Quản lý từ xa mới ở mức xem và ra lệnh

Web chỉ hiện trạng thái và bấm Chạy / Dừng / Khởi động lại. Không sửa config hay lịch, không xem
output, hub không lưu lịch sử, một tài khoản duy nhất. Chi tiết ở [06-quan-ly-tu-xa.md](06-quan-ly-tu-xa.md).
Phần "không sửa từ xa" là chủ ý bảo mật chứ không phải chưa kịp làm.

## Hướng phát triển

Xếp theo giá trị mang lại trên công sức bỏ ra.

### Ưu tiên cao

**Theo dõi file cấu hình bị sửa bên ngoài.** `FileSystemWatcher` trên các file đang mở, hiện cảnh
báo "file đã đổi trên đĩa, tải lại?" — bịt đúng lỗ hổng ghi đè nói ở trên.

### Ưu tiên trung bình

**Xem output từ xa.** Agent đẩy các dòng output lên hub theo lô, web hiện cho app đang chọn. Cần
giới hạn tốc độ vì output có thể rất dày.

**Lịch sử chạy trên hub.** Agent gửi `AppRunRecord` khi kết thúc; hub lưu SQLite để xem xuyên máy.

**Thêm/xoá khoá trong chế độ bảng.** Bắt đầu từ INI (dễ nhất: chèn dòng vào section), rồi JSON.

**Hồ sơ môi trường (profile).** Một bộ biến môi trường dùng chung cho nhiều app, thay vì lặp lại
`DB_HOST` ở từng app.

**Xuất/nhập cấu hình.** Xuất một app (hoặc cả workspace) ra file để chia sẻ cho máy khác, có tuỳ
chọn thay thế đường dẫn gốc.

**Tô màu cú pháp cho chế độ sửa nguồn.** AvalonEdit là lựa chọn hợp lý cho WPF.

### Ưu tiên thấp / cần cân nhắc

**Windows Service chạy lịch.** Giải quyết triệt để giới hạn lớn nhất, nhưng đổi lại: cần quyền admin
để cài, phải tách logic lịch sang tiến trình riêng, cần kênh IPC giữa service và giao diện, và phải
xử lý chuyện service chạy dưới tài khoản khác thì `%APPDATA%` cũng khác.

Phương án nhẹ hơn cho cùng mục tiêu: Cowork **sinh ra Task trong Windows Task Scheduler** thay vì tự
chạy lịch. Mất khả năng theo dõi realtime, nhưng được độ tin cậy của hệ điều hành. Có thể làm song
song: app nào chọn "lịch hệ thống" thì đăng ký sang Task Scheduler.

**Bảng điều khiển dạng lưới.** Thay danh sách dọc bằng lưới thẻ, xem trạng thái nhiều app cùng lúc.

**Nhiều workspace.** Tách bộ app theo dự án hoặc theo khách hàng.

## Ghi chú cho người phát triển tiếp

**Nơi nên thêm code**

| Muốn thêm gì | Sửa ở đâu |
|---|---|
| Định dạng config mới (YAML, TOML) | Cài `IConfigEditor`, đăng ký trong constructor của `ConfigFileService`, thêm phần mở rộng vào `ConfigFormatExtensions.Resolve` |
| Kiểu lịch mới | Thêm giá trị vào `ScheduleKind`, xử lý trong `ScheduleRule.GetNextOccurrence` và `ScheduleEvaluator.IsDue`, thêm RadioButton ở tab Lịch chạy |
| Quy tắc kiểm tra mới | `AppValidator.Validate` — nhớ phân biệt lỗi (chặn lưu) và cảnh báo |
| Mẫu nhận diện file cấu hình mới | `ConfigFileScanner`: thêm vào `CandidateExtensions`, `NoiseDirectories` hoặc hàm `Score` |
| Mẫu nhận diện chương trình mới | `ProgramScanner`: thêm vào `RunnableExtensions`, `LauncherNames` hoặc hàm `Score`. Lưu ý danh sách thư mục nhiễu ở đây **ngược** với bộ quét cấu hình |
| Trường mới trên app | `ManagedApp` + `Clone()` + `AppViewModel` + XAML. `JsonWorkspaceStore.Normalize` lo phần tương thích ngược |
| Kiểu thăm dò sức khoẻ mới | Thêm giá trị vào `HealthProbeKind`, xử lý trong `NetworkHealthProbe.ProbeAsync`, thêm quy tắc vào `HealthTarget.IsValid`, khai nhãn `HealthProbe.<tên>` ở cả hai bảng ngôn ngữ |
| Kiểu chờ phụ thuộc mới | Thêm giá trị vào `DependencyWait`, xử lý trong `RunQueue.Evaluate`, thêm quy tắc vào `DependencyGraph.Validate`, khai nhãn `DependencyWait.<tên>` ở cả hai bảng ngôn ngữ |

**Ràng buộc phải giữ**

1. `Cowork.Core` **không được** tham chiếu WPF hay `System.Windows`. Đây là thứ giữ cho logic test
   được và cho phép làm bản CLI/Service sau này.
2. Mọi sự kiện từ tầng dưới **phải** đi qua `Dispatcher` trước khi chạm view-model.
3. `ApplyChanges` **phải** vá trên text gốc, không dựng lại file từ bảng.
4. Mọi thao tác ghi file **phải** qua file tạm rồi `File.Replace`.
5. Logic quyết định về thời gian **phải** nhận `now` làm tham số, không gọi `DateTime.Now` bên trong.

**Chạy test**

```bash
dotnet test                                        # toàn bộ test
dotnet test --filter FullyQualifiedName~Schedule   # chỉ nhóm lịch
```

Nhóm `ProcessManagerTests` khởi chạy `cmd.exe` thật — chậm hơn (~1 giây) nhưng đó là chỗ duy nhất
kiểm chứng được luồng khởi chạy, thu output, biến môi trường và dừng tiến trình hoạt động thật sự.
