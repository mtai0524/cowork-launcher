# 04 — Hướng dẫn sử dụng

## Giao diện

```
┌─ Thanh công cụ ─────────────────────────────────────────────────────┐
│ Cowork │ ▶Chạy  ■Dừng │ Chạy tất cả  Dừng tất cả  Kiểm tra lịch      │
│                                          ☑Chạy theo lịch  [ Lưu ]   │
├────────────────┬────────────────────────────────────────────────────┤
│ Danh sách app  │ Tổng quan  Biến môi trường  File cấu hình           │
│ [tìm kiếm...]  │ Lịch chạy  Nhật ký  Lịch sử chạy  Thiết lập         │
│                │                                                    │
│ ● App A        │                                                    │
│ ● App B        │            (nội dung tab đang chọn)                │
│                │                                                    │
│ +Thêm Nhân bản │                                                    │
│ Xoá   ↑    ↓   │                                                    │
├────────────────┴────────────────────────────────────────────────────┤
│ Sẵn sàng.                                    ● Có thay đổi chưa lưu │
└─────────────────────────────────────────────────────────────────────┘
```

Màu chấm bên trái mỗi app: **xám** chưa chạy · **vàng** đang khởi động/dừng · **xanh** đang chạy ·
**đỏ** lỗi.

Phím tắt: `Ctrl+S` lưu · `F5` chạy app đang chọn · `Ctrl+N` thêm app mới.

> Thanh trạng thái hiện "● Có thay đổi chưa lưu" nghĩa là cấu hình mới nằm trong bộ nhớ. Bấm **Lưu**
> để ghi xuống `workspace.json`. Cowork cũng tự lưu khi thoát.

## Thêm app đầu tiên

1. Bấm **+ Thêm**.
2. Tab **Tổng quan** → **Chọn…** ở ô *Chương trình*, trỏ tới `.exe` / `.bat` / `.cmd` / `.ps1`.
   Tên app tự điền theo tên file nếu bạn chưa đặt.
3. Điền *Tham số dòng lệnh* nếu cần. Đường dẫn có dấu cách nhớ bọc nháy kép:
   `--input "C:\Du lieu\input.csv"`.
4. *Thư mục làm việc* để trống là được — Cowork dùng thư mục chứa file chương trình.
5. Bấm **▶ Chạy** để thử ngay. Sang tab **Nhật ký** xem output.
6. **Lưu** (`Ctrl+S`).

### Chạy file PowerShell

`.ps1` không tự chạy được bằng cách gọi trực tiếp. Khai báo như sau:

| Ô | Giá trị |
|---|---|
| Chương trình | `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` |
| Tham số | `-NoProfile -ExecutionPolicy Bypass -File "C:\scripts\bao-cao.ps1"` |

## Tìm file cấu hình tự động

Chọn app → tab **File cấu hình** → **Quét thư mục…**. Cowork quét thư mục làm việc của app và liệt
kê những file trông giống cấu hình, kèm mức tin cậy và lý do:

| Mức | Nghĩa | Ví dụ |
|---|---|---|
| **Cao** | Tên file khớp mẫu quen thuộc — tick sẵn cho bạn | `appsettings.json`, `web.config`, `.env`, `.npmrc`, **`config`** (không đuôi) |
| **Vừa** | Phần mở rộng hầu như luôn là cấu hình, nằm trong thư mục `config/`, hoặc là file mẫu | `database.ini`, `render.yaml`, `.env.example`, `config.example` |
| **Thấp** | Chỉ khớp phần mở rộng, hoặc là manifest dự án | `package.json`, `manifest.json`, `data.json` nằm sâu |

Chỉ mức **Cao** được tick sẵn. Bấm **Thêm N file đã chọn** để đưa vào app.

**File cấu hình không có phần mở rộng cũng được tìm thấy.** Rất nhiều công cụ đặt cấu hình ở
`~/.config/<tên app>/config` hoặc `~/.ssh/config` — tên là `config` trơn, không có `.json` hay
`.ini` gì cả. Cowork nhận ra chúng qua tên file, rồi **đọc nội dung để đoán định dạng**: thấy `{` là
JSON, `<` là XML, thấy các dòng `khoá = giá trị` hoặc `[section]` là INI. Không nhận ra hình dạng
nào thì mở ở chế độ sửa nguồn.

Thư mục bắt đầu bằng dấu chấm (`.config`, `.ssh`, `.aws`) **được quét** — chỉ thư mục của công cụ
(`.git`, `.vs`, `.venv`, `.next`…) mới bị bỏ qua.

Cowork tự bỏ qua: `node_modules`, `bin`, `obj`, `.git`, `dist`, `venv`… ; file lock và file sinh tự
động (`package-lock.json`, `project.assets.json`, `*.min.json`); file lớn hơn 2 MB; và file mở đầu
bằng `#!` (script như `configure`, chứ không phải cấu hình). File sai cú pháp vẫn hiện nhưng bị hạ
một bậc tin cậy và tô nền cam.

Chỉnh **Độ sâu tối đa** (mặc định 4 cấp) nếu cấu hình nằm sâu hơn, và tick **Hiện cả kết quả tin cậy
Thấp** nếu chưa thấy file mình cần.

> Quét chỉ *gợi ý*, không bao giờ tự thêm file. File nằm trong thư mục làm việc sẽ được lưu dưới
> dạng đường dẫn tương đối.

## Sửa file cấu hình của app

Nếu file không nằm trong thư mục làm việc hoặc bạn muốn chỉ đích danh:

1. Chọn app → tab **File cấu hình** → **+ Thêm file thủ công**.
2. Bấm **Chọn file…**, trỏ tới file config của app đó.
3. Cowork tự nhận định dạng và hiện bảng khoá–giá trị, gom nhóm theo section.

**Chế độ bảng** — sửa trực tiếp cột *Giá trị*:

- Dòng đã sửa nền **xanh**; dòng nhập sai kiểu (chữ vào ô số) nền **cam** kèm giải thích.
- Ô tìm kiếm phía trên lọc theo tên khoá hoặc giá trị — hữu ích với file hàng trăm dòng.
- Cột *Ghi chú* hiển thị comment nằm ngay trên khoá đó trong file gốc (INI/XML).

**Chế độ sửa nguồn** — sửa text thô. Cowork kiểm tra cú pháp trước khi ghi; sai thì báo lỗi và
**không** ghi đè.

Bấm **Lưu file** để ghi. Mặc định Cowork tạo bản sao `<tên file>.cowork.bak` trước khi ghi đè.

> Chỉ những giá trị bạn đã sửa mới bị đụng tới. Comment, dòng trắng, thứ tự khoá, kiểu xuống dòng
> đều giữ nguyên — người khác `diff` file sẽ chỉ thấy đúng dòng bạn đổi.

### Đường dẫn tương đối

Nếu điền *Thư mục làm việc* = `C:\tools\backup`, bạn có thể khai file config là `config\db.ini` thay
vì đường dẫn đầy đủ. Khi nhân bản app sang thư mục khác, chỉ cần đổi thư mục làm việc.

### Định dạng hỗ trợ

| Định dạng | Ví dụ file | Chế độ bảng |
|---|---|---|
| JSON | `appsettings.json` | ✅ giữ nguyên kiểu số/bool |
| INI / .env / .properties | `config.ini`, `.env`, `.npmrc` | ✅ giữ comment, khoảng trắng, kiểu nháy |
| XML / App.config | `App.config`, `web.config` | ✅ cả thuộc tính lẫn nội dung phần tử |
| Không đuôi | `~/.config/gcm/config` | ✅ nếu đoán được là JSON/XML/INI theo nội dung |
| Khác | `.txt`, `.yaml`, `.toml`, `~/.ssh/config` | Chỉ sửa nguồn |

Định dạng đoán được hiển thị ngay dưới tên file trong danh sách bên trái. Nếu Cowork đoán sai, bạn
không bị kẹt: mở tab, chuyển sang **Sửa nguồn** là sửa được mọi file.

## Đặt lịch chạy

Tab **Lịch chạy** → tick *Bật lịch tự động cho app này* → chọn kiểu:

### Mốc giờ cố định

Nhập nhiều mốc cách nhau bằng dấu phẩy: `07:30, 12:00, 18:15`.

### Chu kỳ lặp

*Mỗi (phút)* = `30`, kèm cửa sổ giờ tuỳ chọn: *Chỉ chạy từ* `08:00` *đến* `18:00`. Ngoài khung này
Cowork không chạy.

### Khi mở Cowork

Chạy một lần ngay lúc Cowork khởi động. Hợp với app nền cần luôn sống.

### Ngày trong tuần

Tick T2–CN. **Không tick ngày nào = chạy mọi ngày.**

### Chạy bù

Tick *Chạy bù nếu lúc tới hạn máy đang tắt* nếu công việc **bắt buộc phải chạy trong ngày** (sao lưu,
báo cáo bắt buộc). Mở Cowork lúc 11h, mốc 7:30 chưa chạy ⇒ chạy ngay.

Không tick nếu công việc **hết giá trị khi trễ** (thông báo nhắc nhở buổi sáng). Quá 10 phút so với
mốc thì bỏ qua, đợi mốc kế tiếp.

Khung tóm tắt cuối tab luôn hiện **Lần chạy kế tiếp** để bạn kiểm chứng lịch đã đúng ý chưa.

> Lịch chỉ chạy khi **Cowork đang mở** (kể cả đã thu nhỏ xuống khay) và công tắc *Chạy theo lịch*
> trên thanh công cụ đang bật. Muốn lịch chạy ngay cả khi chưa đăng nhập Windows, xem
> [05-lo-trinh.md](05-lo-trinh.md).

## Biến môi trường riêng

Tab **Biến môi trường** → **+ Thêm biến**. Các biến này chỉ áp dụng cho tiến trình con do Cowork
khởi chạy — **không** ảnh hưởng hệ thống hay các app khác.

Giá trị hỗ trợ tham chiếu biến khác: `%USERPROFILE%\data`.

Dùng để tách cấu hình theo môi trường: cùng một script, hai app khác `DB_HOST` là có bản chạy thật
và bản chạy thử.

## Chạy nhiều app cùng lúc

**Chạy tất cả** khởi chạy mọi app đang bật, theo thứ tự trên danh sách. Dùng nút **↑ ↓** để sắp
thứ tự ưu tiên.

Thanh trạng thái báo số app đã khởi chạy được. App nào cấu hình còn thiếu sẽ bị bỏ qua kèm lý do,
không chặn những app còn lại.

## Theo dõi

**Tab Nhật ký** — output realtime của app đang chọn, tự cuộn xuống dòng mới; dòng lỗi (stderr) màu đỏ.

Output chỉ có khi bật *Thu nhật ký output* và **không** bật *Chạy với quyền quản trị* (hai thứ này
loại trừ nhau về mặt kỹ thuật). Log đầy đủ luôn được ghi ra `%APPDATA%\Cowork\logs`.

**Tab Lịch sử chạy** — mọi lần chạy: thời điểm, nguồn kích hoạt, kết quả, mã thoát, thời lượng.
Mã thoát `0` = thành công.

## Chạy nền

Tab **Thiết lập**:

- **Bấm X thì thu nhỏ xuống khay** — Cowork tiếp tục chạy lịch dưới khay hệ thống. Nhấp đúp biểu
  tượng khay để mở lại; chuột phải có menu *Chạy tất cả* / *Dừng tất cả* / *Thoát*.
- **Khởi động cùng Windows** — ghi khoá `HKCU\...\Run`, không cần quyền admin. Cowork mở ở chế độ
  thu nhỏ.

## Xử lý sự cố

| Hiện tượng | Nguyên nhân & cách xử lý |
|---|---|
| "Không tìm thấy file" khi chạy | Đường dẫn sai hoặc file đã bị di chuyển. Bấm **Chọn…** trỏ lại. |
| "Bị từ chối quyền truy cập" | Bật *Chạy với quyền quản trị* trong tab Tổng quan. |
| "Người dùng đã huỷ hộp thoại nâng quyền" | Bạn bấm No ở UAC. Chạy lại và chọn Yes. |
| Tab Nhật ký trống | Chưa bật *Thu nhật ký output*, hoặc đang bật *Chạy quyền quản trị*. Xem log trong `%APPDATA%\Cowork\logs`. |
| "App đang chạy, bỏ qua lần khởi chạy này" | `SingleInstance` đang bật và instance cũ còn sống. Dừng nó trước, hoặc tắt tuỳ chọn này. |
| App không tự chạy theo lịch | Kiểm tra: công tắc *Chạy theo lịch* trên thanh công cụ · *Bật lịch tự động* của app · app đang bật · ngày trong tuần có tick. Bấm **Kiểm tra lịch** để chạy ngay một vòng rà soát. |
| Bảng cấu hình trống, có báo lỗi đỏ | File sai cú pháp. Cowork chuyển sang chế độ sửa nguồn để bạn sửa tay. |
| Sửa config xong app vẫn dùng giá trị cũ | App đọc config lúc khởi động. Dừng rồi chạy lại. |
| Lỡ sửa hỏng file config | Khôi phục từ `<tên file>.cowork.bak` nằm cùng thư mục. |
| Lỡ xoá nhầm app, hoặc mất danh sách app | Copy bản chụp gần nhất trong `%APPDATA%\Coworkackups\` đè lên `workspace.json` (đóng Cowork trước). |
| Quét thư mục không thấy file cần tìm | Tăng **Độ sâu tối đa**, tick **Hiện cả kết quả tin cậy Thấp**, hoặc dùng **+ Thêm file thủ công**. |
| Cowork không mở được, mất hết app | `workspace.json` hỏng đã bị đổi tên thành `workspace.json.corrupt-*` trong `%APPDATA%\Cowork`. Sửa cú pháp rồi đổi tên lại. |

## Sao lưu cấu hình Cowork

Cowork tự chụp lại `workspace.json` **một lần vào đầu mỗi ngày**, ngay trước lần lưu đầu tiên, vào
`%APPDATA%\Coworkackups\workspace-YYYYMMDD.json` và giữ 10 bản gần nhất. Nếu lỡ xoá nhầm app
hoặc file chính bị ghi đè, copy bản chụp gần nhất đè lại (nhớ đóng Cowork trước).

Để sao lưu chủ động: copy `%APPDATA%\Cowork\workspace.json`. File này chứa toàn bộ khai báo app, lịch, biến môi trường và
đường dẫn config. Chuyển sang máy khác: đặt lại đúng chỗ đó (nhớ đóng Cowork trước) — với điều kiện
các đường dẫn chương trình trên máy mới giống nhau.
