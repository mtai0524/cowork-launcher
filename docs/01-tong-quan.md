# 01 — Tổng quan

## Bài toán

Mỗi sáng bạn phải mở lần lượt vài chương trình: script sao lưu CSDL, tool xuất báo cáo, dịch vụ
đồng bộ dữ liệu. Mỗi cái nằm một thư mục, cần tham số riêng, và thỉnh thoảng phải sửa file cấu hình
(đổi IP server, đổi đường dẫn xuất file, bật/tắt chế độ debug).

Cách làm thủ công có ba điểm đau:

1. **Nhớ đường dẫn.** Chương trình nào nằm ở đâu, chạy với tham số gì — trí nhớ hoặc file ghi chú.
2. **Sửa config.** Phải mò vào thư mục, mở Notepad, sửa đúng dòng, tự nhớ sao lưu trước khi sửa.
3. **Không biết đã chạy chưa.** Sáng nay đã chạy backup chưa? Chạy có lỗi không?

Task Scheduler của Windows giải được phần 1 và một phần của 3, nhưng nó không giúp gì cho phần 2,
và giao diện của nó không phải chỗ để xem nhanh "hôm nay app nào lỗi".

## Cowork giải quyết thế nào

Một cửa sổ duy nhất chứa đủ ba việc:

- **Khai báo một lần** — mỗi app là một mục có tên, đường dẫn, tham số, biến môi trường.
- **Sửa config tại chỗ** — mỗi app khai báo kèm các file cấu hình của nó; Cowork mở ra dạng bảng
  khoá-giá trị để sửa, tự sao lưu trước khi ghi.
- **Chạy và theo dõi** — bấm chạy, hoặc đặt lịch để Cowork tự chạy; xem output realtime và lịch sử
  các lần chạy trước.

## Phạm vi

### Có làm

- Quản lý danh sách app cần chạy hằng ngày trên **máy cục bộ**.
- Khởi chạy `.exe`, `.bat`, `.cmd`, `.ps1` với tham số, thư mục làm việc, biến môi trường riêng.
- Đọc–sửa–ghi file cấu hình định dạng **JSON, INI/.env/.properties, XML/App.config**, và sửa text
  thô cho mọi định dạng khác.
- **Quét thư mục để tự dò tìm file cấu hình**, chấm điểm tin cậy và loại bỏ nhiễu
  (`node_modules`, `bin`, file lock, manifest dự án).
- Lịch chạy: mốc giờ cố định, chu kỳ lặp, chạy khi mở Cowork; lọc theo ngày trong tuần.
- Chạy theo sự kiện của máy: thức dậy sau khi ngủ, mở khoá màn hình, có mạng trở lại.
- Giữ app luôn chạy: tự khởi động lại khi app thoát, có trần số lần mỗi giờ.
- Bắt app treo: thăm dò cổng/URL định kỳ, theo dõi output im lặng hoặc chứa mẫu báo lỗi.
- Phụ thuộc giữa các app: "Chạy tất cả" chờ đúng thứ tự, phụ thuộc lỗi thì bỏ qua phần phía sau.
- Thử lại job lỗi: kết thúc lỗi hay quá giờ thì chạy lại vài lần rồi mới báo; mã thoát nào là thành
  công đặt được cho từng app.
- Dừng lịch sự: đóng cửa sổ chính, hoặc gửi Ctrl+C cho app console, hết thời gian ân hạn mới kill.
- Nhật ký output, lịch sử chạy, log ra file, thông báo ở khay khi app lỗi.
- Chạy nền dưới khay hệ thống, khởi động cùng Windows.
- Quản lý từ xa: nhiều máy nối ra một hub web, xem trạng thái và bấm Chạy / Dừng / Khởi động lại.

### Không làm (có chủ ý)

- **Không sửa cấu hình từ xa.** Hub web chỉ xem trạng thái và ra lệnh Chạy / Dừng; sửa đường dẫn,
  tham số, lịch hay file cấu hình vẫn phải làm trên chính máy đó. Đây là chủ ý về bảo mật: tài khoản
  web bị lộ thì cũng chỉ kích hoạt được app đã khai sẵn — xem [06-quan-ly-tu-xa.md](06-quan-ly-tu-xa.md).
- **Không thay thế Windows Service.** Cowork phải đang chạy thì lịch mới hoạt động — đây là đánh đổi
  để mọi thứ nằm trong quyền người dùng, không cần cài dịch vụ hệ thống. Xem
  [05-lo-trinh.md](05-lo-trinh.md).
- **Không phải trình soạn thảo code.** Chế độ sửa nguồn là ô text thuần, không tô màu cú pháp.
- **Không tự thêm file cấu hình.** Cowork *có* quét thư mục và gợi ý file nào trông giống cấu hình,
  nhưng luôn để bạn tick chọn trước khi thêm — đoán sai ở đây dẫn tới ghi đè nhầm file, hậu quả nặng
  hơn nhiều so với việc phải tick vài ô.

## Khái niệm

| Khái niệm | Nghĩa |
|---|---|
| **App** (`ManagedApp`) | Một chương trình do Cowork quản lý: lệnh chạy + config + lịch |
| **Workspace** | Toàn bộ danh sách app và thiết lập chung, lưu trong `workspace.json` |
| **File cấu hình** (`ConfigFileRef`) | Con trỏ tới một file config thuộc về app; nội dung luôn đọc từ đĩa |
| **Lịch** (`ScheduleRule`) | Quy tắc app tự chạy: kiểu lịch + mốc giờ + ngày trong tuần |
| **Mốc chạy** (occurrence) | Một thời điểm cụ thể mà lịch quy định app phải chạy |
| **Lần chạy** (`AppRunRecord`) | Một lượt khởi chạy: bắt đầu lúc nào, mã thoát bao nhiêu, do ai kích hoạt |
| **Kiểm tra sức khoẻ** (`HealthCheck`) | Cách nhận ra app còn sống nhưng đã treo: thăm dò cổng/URL, hoặc theo dõi output |
| **Phụ thuộc** (`AppDependency`) | App phải sẵn sàng trước, và "sẵn sàng" nghĩa là *chạy xong* hay chỉ *đã lên* |
| **Trạng thái** (`AppRuntimeState`) | Idle / Starting / Running / Stopping / Failed / WaitingRestart — chỉ tồn tại trong bộ nhớ |

## Nguyên tắc thiết kế

**File trên đĩa là nguồn sự thật.** Cowork không cache nội dung file cấu hình. Mỗi lần bạn mở một
file, nó được đọc lại từ đĩa. Nếu ai đó sửa file bằng công cụ khác, bấm "Tải lại" là thấy ngay.

**Sửa tối thiểu.** Khi ghi lại file cấu hình, Cowork chỉ đụng vào đúng những giá trị bạn đã sửa.
Comment, dòng trắng, thứ tự khoá, kiểu xuống dòng, cách đặt nháy — giữ nguyên hết.

**Không im lặng nuốt lỗi.** File config sai cú pháp thì hiện lỗi và chuyển sang chế độ sửa nguồn,
chứ không hiện bảng rỗng. App không chạy được thì nói rõ lý do (không tìm thấy file, bị từ chối
quyền, người dùng huỷ UAC…), chứ không chỉ báo "thất bại".

**Logic quyết định phải test được.** Câu hỏi "app này đã tới giờ chạy chưa" là hàm thuần
`ScheduleEvaluator.IsDue(app, now)`, không đọc đồng hồ hệ thống bên trong — nên test được mọi tình
huống thời gian mà không phải chờ.
