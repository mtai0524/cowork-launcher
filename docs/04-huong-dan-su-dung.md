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

### Tìm chương trình tự động

Nếu chỉ nhớ mang máng app nằm ở thư mục nào, bấm **Quét thư mục…** cạnh ô *Chương trình*. Cowork dò
mọi file `.exe` / `.bat` / `.cmd` / `.ps1` trong thư mục đó và xếp hạng:

| Mức | Căn cứ | Ví dụ |
|---|---|---|
| **Cao** | Tên trùng tên thư mục, hoặc là từ khoá khởi chạy | `mytool\mytool.exe`, `run.bat`, `start.cmd`, `chay-backup.bat` |
| **Vừa** | Nằm ngay thư mục gốc, hoặc trong thư mục build | `helper.exe`, `bin\Release\app.exe`, `dist\packer.exe` |
| **Thấp** | Nằm sâu trong cây thư mục, hoặc là trình cài đặt | `unins000.exe`, `setup.exe`, `tools\deep\misc.exe` |

Chọn một dòng (hoặc nháy đúp) rồi bấm **Dùng chương trình này**. Khung *Lệnh sẽ được điền vào app*
phía dưới cho xem trước chính xác thứ sẽ được ghi vào.

Khác với quét file cấu hình, ở đây `bin`, `dist`, `build`, `publish` **được** quét — đó chính là nơi
file `.exe` nằm. Cowork bỏ qua `node_modules`, `obj`, `.git`, và tàn dư build (`*.vshost.exe`,
`crashpad_handler.exe`).

Sau khi chọn, Cowork tự điền thêm *Thư mục làm việc* (thư mục chứa chương trình) và *Tên app* nếu
bạn chưa đặt.

### Chạy file PowerShell

`.ps1` không tự chạy được bằng cách gọi trực tiếp. Nếu chọn `.ps1` từ hộp thoại quét, **Cowork tự
bọc giúp** — bạn không phải làm gì thêm. Khai báo tay thì như sau:

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

## Chạy khi có sự kiện của máy

Lịch theo đồng hồ bỏ lỡ đúng lúc đáng chạy nhất: laptop vừa mở nắp, mạng vừa có lại sau khi đứt.
Tab **Lịch chạy → Chạy khi có sự kiện của máy** có ba mốc, tick bao nhiêu cái cũng được:

| Sự kiện | Xảy ra khi |
|---|---|
| **Khi máy thức dậy sau khi ngủ** | Mở nắp laptop, đánh thức máy sau sleep/hibernate |
| **Khi mở khoá màn hình** | Gõ mật khẩu vào màn hình khoá, hoặc đăng nhập lại |
| **Khi có mạng trở lại** | Cắm lại dây mạng, Wi-Fi kết nối lại |

Phần này **độc lập với kiểu lịch ở trên** — một app vừa chạy 07:30 hằng ngày, vừa chạy mỗi khi máy
ngủ dậy là hoàn toàn được.

*Chờ sau sự kiện (giây)* mặc định 15: ngay lúc thức dậy, card mạng và dịch vụ hệ thống chưa sẵn
sàng, chạy ngay thường lỗi. Tăng lên nếu app cần VPN hay ổ đĩa mạng.

Cowork bỏ qua sự kiện trùng trong vòng **một phút** cho từng cặp app–sự kiện, vì Windows hay bắn
`Resume` vài lần cho một lần mở nắp máy. App đang chạy và có bật *Không chạy chồng* thì cũng bỏ qua.
Lịch sử ghi nguồn kích hoạt là **Sự kiện máy**.

> Khác với *Chạy bù*: chạy bù bù lại **mốc giờ đã lỡ**, còn mục này chạy vì **chính sự kiện đó**.
> Việc cần chạy đúng một lần mỗi ngày thì dùng chạy bù; việc cần chạy mỗi lần máy tỉnh dậy
> (đồng bộ, nối lại VPN) thì dùng mục này.

## Biến môi trường riêng

Tab **Biến môi trường** → **+ Thêm biến**. Các biến này chỉ áp dụng cho tiến trình con do Cowork
khởi chạy — **không** ảnh hưởng hệ thống hay các app khác.

Giá trị hỗ trợ tham chiếu biến khác: `%USERPROFILE%\data`.

Dùng để tách cấu hình theo môi trường: cùng một script, hai app khác `DB_HOST` là có bản chạy thật
và bản chạy thử.

## Giữ app luôn chạy

Dành cho app kiểu dịch vụ — server cục bộ, tunnel, bot — thứ phải *đang chạy* chứ không phải *đã
chạy xong*. Tab **Tổng quan → Tuỳ chọn khi chạy**, tick **Giữ app luôn chạy**.

Từ đó, mỗi khi tiến trình tự thoát (crash, thoát êm, hay bị dừng vì quá giờ), Cowork chờ số giây
đã đặt rồi chạy lại. Danh sách app hiện *Sẽ khởi động lại sau 5s (lần 2)* trong lúc đếm ngược; lịch
sử ghi nguồn kích hoạt là **Tự khởi động lại**.

Hai chỗ Cowork **không** khởi động lại, cố ý:

- **Bạn bấm Dừng.** Dừng tay là ý bạn. Bấm Dừng trong lúc đang đếm ngược cũng huỷ luôn lần chờ.
- **App chưa hề chạy được** (thiếu file, bị từ chối quyền). Chạy lại cũng thế, chỉ tạo vòng lặp.

**Tối đa (lần / giờ)** là lưới an toàn: một app hỏng hẳn sẽ crash ngay sau khi lên, và không có
trần thì Cowork sẽ khởi động lại nó vô tận. Chạm trần, Cowork bỏ cuộc, đánh dấu app lỗi và báo ở
khay. Bấm **Chạy** để thử lại bằng tay. Đặt `0` nếu bạn thật sự muốn không giới hạn.

Muốn app tự lên ngay khi mở Cowork thì kết hợp với lịch **Chạy một lần khi mở Cowork** — keep-alive
chỉ lo phần *giữ*, không lo phần *khởi động lần đầu*.

> Mẹo: **Giữ luôn chạy** + **Tự dừng sau N phút** = tự khởi động lại định kỳ mỗi N phút, tiện cho
> app rò rỉ bộ nhớ.

Keep-alive không biết app *treo* — thứ đó thuộc về [Bắt app treo](#bắt-app-treo) ngay dưới; hai mục
đi cùng nhau thì app đơ mới được khởi động lại.

## Bắt app treo

Keep-alive chỉ thấy app *thoát*. App còn sống nhưng đơ — deadlock, mất kết nối CSDL, vòng lặp treo —
vẫn được tính là đang chạy. Tab **Tổng quan → Kiểm tra sức khoẻ** có hai cách nhận ra, dùng riêng
hoặc chung.

**Thăm dò cổng / URL.** Chọn *Thăm dò* rồi điền *Mục tiêu*:

| Kiểu | Mục tiêu | Còn sống nghĩa là |
|---|---|---|
| **Cổng TCP** | `8080` hoặc `db.local:5432` | Mở được kết nối |
| **Địa chỉ HTTP** | `http://localhost:8080/health` | Trả về mã 2xx |

Cowork thăm dò mỗi *Kiểm tra mỗi (giây)*. Quá *Chờ phản hồi tối đa* mà im, hoặc trả về mã lỗi, thì
tính là một lần hỏng; đủ *Lỗi liên tiếp trước khi coi là treo* lần liên tiếp mới kết luận. Một lần
thành công xen giữa là đếm lại từ đầu — mạng chập một nhịp không giết app đang khoẻ.

*Bỏ qua kiểm tra trong N giây đầu* để app kịp mở cổng; server nặng nên để 60–120 giây.

**Watchdog theo output.** Không có cổng nào để thăm dò thì nhìn vào nhật ký:

- *Coi là treo nếu không có output trong N phút* — hợp với app in log đều đặn. Để `0` để tắt.
- *Mẫu báo lỗi trong output* — mỗi dòng một mẫu, ví dụ `FATAL`, `OutOfMemory`, `connection lost`.
  Là regex, không phân biệt hoa thường; gõ sai cú pháp regex thì Cowork so như chuỗi thường và
  cảnh báo chứ không chặn. Một dòng khớp là dừng app ngay, không cần chờ nhịp kiểm tra.

> Watchdog theo output cần bật *Thu nhật ký output* và **không** bật *Chạy với quyền quản trị* —
> Windows không cho đọc output của tiến trình nâng quyền. Thiếu điều kiện thì Cowork cảnh báo và bỏ
> qua phần này; thăm dò cổng/URL vẫn chạy bình thường.

Phát hiện treo, Cowork dừng app (lịch sự trước, theo *Chờ dừng lịch sự tối đa*), lịch sử ghi kết quả
**Treo** kèm lý do. Từ đó trở đi mọi thứ giống hệt một lần crash: app bật *Giữ luôn chạy* được khởi
động lại, app có đặt *Thử lại khi lỗi* được chạy lại, còn lại thì báo ở khay.

## Thử lại khi job lỗi

Dành cho job **chạy xong là thoát** — sao lưu, đồng bộ, xuất báo cáo — thứ hay lỗi vì mạng chập chờn
hay server bận rồi tự hết. Tab **Tổng quan → Tuỳ chọn khi chạy → Thử lại khi lỗi**: đặt *Số lần thử
lại* và *Chờ trước khi thử lại (giây)*.

Khi một lần chạy kết thúc **lỗi** (mã thoát không nằm trong danh sách thành công), bị **quá giờ**,
hoặc bị dừng vì **treo**, Cowork chờ rồi chạy lại, tối đa số lần đã đặt. Danh sách app hiện *Sẽ thử lại sau 30s (lần 1/3)*;
lịch sử ghi nguồn kích hoạt là **Thử lại**. Lần nào thành công thì chuỗi kết thúc. Hết lượt mà vẫn lỗi
thì app chuyển sang trạng thái lỗi và khay hệ thống báo **một lần** — các lần lỗi giữa chừng không báo,
vì xử lý chúng chính là việc của thử lại.

Ba chỗ Cowork **không** thử lại, cố ý:

- **Bạn bấm Dừng.** Bấm trong lúc đang đếm ngược thì lần chờ bị huỷ luôn.
- **App chưa hề chạy được** (thiếu file, bị từ chối quyền). Chạy lại cũng thế.
- **App đang bật Giữ app luôn chạy.** Keep-alive đã khởi động lại rồi, hai cơ chế chồng nhau chỉ gây
  rối; mục này bị mờ đi khi keep-alive đang bật.

Bấm **Chạy** tay trong lúc chờ sẽ huỷ lần thử lại đang chờ và mở một chuỗi mới.

## Mã thoát coi là thành công

Mặc định chỉ mã `0` là thành công. Một số công cụ dùng mã khác để báo "xong việc": `robocopy` trả `1`
khi đã sao chép được file, trình cài đặt trả `3010` khi cần khởi động lại máy. Với những app đó, điền
*Mã thoát coi là thành công* ở **Tổng quan → Tuỳ chọn khi chạy**, cách nhau bằng dấu phẩy: `0, 1`.
Lịch sử, thông báo ở khay và thử lại đều dựa trên danh sách này.

## Dừng app thế nào

Bấm **Dừng**, Cowork làm theo thứ tự:

1. App có cửa sổ chính ⇒ gửi yêu cầu đóng cửa sổ, như bấm nút X.
2. App console (node, python, script `.bat`…) không có cửa sổ ⇒ gửi **Ctrl+C**, đúng tín hiệu mà
   server console dùng để đóng kết nối, ghi nốt dữ liệu rồi thoát. Tín hiệu tới cả cây tiến trình,
   nên server nằm trong một file `.bat` cũng nhận được.
3. Chờ *Chờ dừng lịch sự tối đa (giây)* — mặc định 5 — rồi mới buộc dừng cả cây tiến trình.

Đặt số giây này cao hơn cho app cần thời gian dọn dẹp (server đang xả kết nối, job đang ghi file).
Nếu cả đóng cửa sổ lẫn Ctrl+C đều không gửi được (app GUI chưa lên cửa sổ, app chạy quyền admin),
Cowork buộc dừng ngay.

## Khởi động lại nhanh

Nút **↻ Khởi động lại** trên thanh công cụ dừng app đang chọn (lịch sự rồi mới kill) và chạy lại
ngay — thay cho ba thao tác Dừng, chờ, Chạy. Dùng sau khi sửa file cấu hình, vì app chỉ đọc config
lúc khởi động.

## Chạy nhiều app cùng lúc

**Chạy tất cả** khởi chạy mọi app đang bật, theo thứ tự trên danh sách. Dùng nút **↑ ↓** để sắp
thứ tự ưu tiên.

Thanh trạng thái báo số app đã khởi chạy được. App nào cấu hình còn thiếu sẽ bị bỏ qua kèm lý do,
không chặn những app còn lại.

## Bắt app này chờ app kia

Mặc định "Chạy tất cả" bung mọi app cùng lúc. Muốn *xuất báo cáo* chỉ chạy sau khi *sao lưu CSDL*
xong, mở tab **Tổng quan → Phụ thuộc** của app đi sau và tick app phải chờ.

Cột bên phải chọn "sẵn sàng" nghĩa là gì:

| Chờ tới mức | Khi nào dùng |
|---|---|
| **Chạy xong, thành công** | Job nối tiếp nhau: sao lưu xong mới nén, nén xong mới gửi đi |
| **Đã lên là đủ** | Dịch vụ: chờ server cục bộ hoặc tunnel lên rồi mới chạy client |

Dịch vụ bật *Giữ app luôn chạy* không bao giờ "xong việc", nên chờ nó **chạy xong** là chờ mãi.
Cowork chặn thẳng trường hợp đó: hiện cảnh báo đỏ ngay trong thẻ và bỏ qua app phía sau thay vì
treo cả lượt chạy.

Trong lượt chạy:

- App chỉ lên khi mọi thứ nó chờ đã sẵn sàng; những app không ràng buộc gì vẫn lên ngay như trước.
- Phụ thuộc **lỗi**, bị **bỏ qua**, hay **không chạy được** thì app phía sau cũng bị bỏ qua kèm lý
  do — chạy vào khoảng không còn tệ hơn không chạy. Cả chuỗi phía sau bị bỏ theo.
- Phụ thuộc có đặt *Thử lại khi lỗi* thì Cowork chờ hết chuỗi thử lại rồi mới kết luận.
- Xong lượt, thanh trạng thái tổng kết *bao nhiêu thành công, lỗi, bị bỏ qua*; có app bị bỏ qua thì
  khay hệ thống báo một lần.

Hai chỗ **không** áp dụng phụ thuộc, cố ý:

- **Bấm ▶ Chạy cho một app.** Bạn đang chỉ đích danh app đó, Cowork không tự kéo theo thứ khác.
- **Lịch chạy.** Mỗi app tới giờ là chạy riêng. Muốn một chuỗi chạy theo lịch, đặt lịch cho app đầu
  chuỗi và để các app sau chờ nó trong lượt "Chạy tất cả" — hoặc bấm Chạy tất cả theo lịch của bạn.

Vòng lặp phụ thuộc (A chờ B, B chờ A) được phát hiện ngay: thẻ **Phụ thuộc** hiện cảnh báo đỏ, và
lượt chạy bỏ qua đúng những app trong vòng lặp, phần còn lại vẫn chạy bình thường.

## Theo dõi

**Tab Nhật ký** — output realtime của app đang chọn, tự cuộn xuống dòng mới; dòng lỗi (stderr) màu đỏ.

Output chỉ có khi bật *Thu nhật ký output* và **không** bật *Chạy với quyền quản trị* (hai thứ này
loại trừ nhau về mặt kỹ thuật). Log đầy đủ luôn được ghi ra `%APPDATA%\Cowork\logs`.

**Tab Lịch sử chạy** — mọi lần chạy: thời điểm, nguồn kích hoạt, kết quả, mã thoát, thời lượng.
Mã thoát `0` = thành công.

**Thông báo ở khay** — khi một app chạy theo lịch (hoặc app đang giữ luôn chạy) kết thúc lỗi, Cowork
hiện bong bóng ở khay hệ thống kể cả khi đang thu nhỏ; bấm vào bong bóng để mở lại cửa sổ. Chạy tay
thì không báo, vì bạn đang nhìn thanh trạng thái. App có đặt *Thử lại khi lỗi* chỉ báo khi hết lượt
vẫn lỗi. Tắt ở tab **Thiết lập → Chạy nền** nếu thấy phiền.

## Chạy nền

Tab **Thiết lập**:

- **Bấm X thì thu nhỏ xuống khay** — Cowork tiếp tục chạy lịch dưới khay hệ thống. Nhấp đúp biểu
  tượng khay để mở lại; chuột phải có menu *Chạy tất cả* / *Dừng tất cả* / *Thoát*.
- **Khởi động cùng Windows** — ghi khoá `HKCU\...\Run`, không cần quyền admin. Cowork mở ở chế độ
  thu nhỏ.

## Dọn lịch sử và log

Tab **Thiết lập → Lịch sử & nhật ký** có hai ô: *Số ngày giữ lịch sử chạy* và *Số ngày giữ file log*
(mặc định đều 30). Mỗi app mỗi ngày sinh một file log trong `%APPDATA%\Cowork\logs`, nên app giữ luôn
chạy in log liên tục sẽ chiếm đĩa dần. Cowork dọn lúc mở và vào đầu mỗi ngày khi đang chạy dưới khay;
chỉ file do Cowork tự sinh mới bị xoá, file khác chép vào thư mục đó được để nguyên.

## Xử lý sự cố

| Hiện tượng | Nguyên nhân & cách xử lý |
|---|---|
| "Không tìm thấy file" khi chạy | Đường dẫn sai hoặc file đã bị di chuyển. Bấm **Chọn…** trỏ lại. |
| "Bị từ chối quyền truy cập" | Bật *Chạy với quyền quản trị* trong tab Tổng quan. |
| "Người dùng đã huỷ hộp thoại nâng quyền" | Bạn bấm No ở UAC. Chạy lại và chọn Yes. |
| Tab Nhật ký trống | Chưa bật *Thu nhật ký output*, hoặc đang bật *Chạy quyền quản trị*. Xem log trong `%APPDATA%\Cowork\logs`. |
| "App đang chạy, bỏ qua lần khởi chạy này" | `SingleInstance` đang bật và instance cũ còn sống. Dừng nó trước, hoặc tắt tuỳ chọn này. |
| App không tự chạy theo lịch | Kiểm tra: công tắc *Chạy theo lịch* trên thanh công cụ · *Bật lịch tự động* của app · app đang bật · ngày trong tuần có tick. Bấm **Kiểm tra lịch** để chạy ngay một vòng rà soát. |
| "đã tự khởi động lại N lần trong 60 phút, tạm dừng giữ chạy" | App crash liên tục ngay sau khi lên — thường là lỗi cấu hình hoặc thiếu phụ thuộc. Xem tab Nhật ký / Lịch sử để biết mã thoát, sửa nguyên nhân rồi bấm **Chạy**. |
| Bật *Giữ luôn chạy* nhưng app không lên khi mở Cowork | Keep-alive chỉ *giữ* app đã chạy. Đặt thêm lịch **Chạy một lần khi mở Cowork**. |
| App đơ nhưng Cowork vẫn báo đang chạy | Keep-alive chỉ thấy app *thoát*. Đặt **Kiểm tra sức khoẻ** ở tab Tổng quan: thăm dò cổng/URL, hoặc watchdog theo output. |
| Lịch sử báo **Treo** mà app vẫn tốt | Ngưỡng quá gắt: tăng *Lỗi liên tiếp*, *Chờ phản hồi tối đa*, *Bỏ qua kiểm tra trong N giây đầu*, hoặc nới *không có output trong N phút*. Lý do cụ thể nằm ở cột Ghi chú. |
| Đặt watchdog theo output mà không thấy tác dụng | Cần bật *Thu nhật ký output* và tắt *Chạy với quyền quản trị*. Cowork cảnh báo ngay dưới ô cấu hình khi thiếu. |
| Tick "khi máy thức dậy" mà app không chạy | Cowork phải đang mở (kể cả dưới khay). Kiểm tra thêm: app đang bật · không phải vừa chạy vì đúng sự kiện đó trong vòng một phút · không đang chạy sẵn với *Không chạy chồng*. |
| Đặt phụ thuộc nhưng bấm ▶ Chạy vẫn chạy ngay | Phụ thuộc chỉ áp dụng cho **Chạy tất cả**. Bấm Chạy cho một app là chỉ đích danh app đó. |
| "… bị bỏ qua vì … không thành công" | Đúng như tên gọi: app đi trước lỗi nên app sau không chạy. Sửa app đi trước rồi Chạy tất cả lại. |
| "… chờ một app không nằm trong lượt chạy" | App được chờ đang bị tắt, hoặc đã bị xoá. Bật lại nó, hoặc bỏ tick trong thẻ Phụ thuộc. |
| App chạy đúng nhưng lịch sử báo lỗi, khay hiện thông báo | Công cụ trả mã thoát khác 0 khi thành công (robocopy trả 1). Thêm mã đó vào *Mã thoát coi là thành công* ở tab Tổng quan. |
| Đặt số lần thử lại nhưng app lỗi không thấy chạy lại | Kiểm tra: app không bật *Giữ luôn chạy* · lần chạy đó kết thúc lỗi hay quá giờ, chứ không phải bị bấm Dừng hay không chạy được. |
| Bấm Dừng mà app console mất vài giây mới dừng | Cowork đang chờ app tự thoát sau Ctrl+C. Giảm *Chờ dừng lịch sự tối đa* nếu app không cần dọn dẹp. |
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
