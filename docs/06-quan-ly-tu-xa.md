# 06 — Quản lý từ xa

Nhiều máy, mỗi máy chạy Cowork, xem trạng thái và bấm Chạy / Dừng / Khởi động lại từ một trang web.

## Mô hình

```
 Máy A ─ Cowork (khay) ──┐   agent gọi RA, WebSocket/SignalR
 Máy B ─ Cowork (khay) ──┼──▶  Cowork Hub  ◀── trình duyệt
 Máy C ─ Cowork (khay) ──┘   (một máy luôn bật, có HTTPS)
```

Ba điều quyết định thiết kế:

- **Chính Cowork đang chạy dưới khay là agent.** Không có tiến trình nào thêm. Vì vậy máy phải có
  người đăng nhập và Cowork phải đang mở — bật *Khởi động cùng Windows* + *Thu nhỏ xuống khay*.
  Đổi lại, app GUI chạy từ xa vẫn hiện cửa sổ bình thường trên máy đó.
- **Agent gọi ra hub, không phải hub gọi vào máy.** Máy trong nhà nằm sau NAT, đổi IP, có firewall;
  hub không với tới được, nhưng máy nào cũng gọi ra được. Không mở port nào trên các máy.
- **Hub không lưu gì.** Nó chỉ phản ánh những gì agent đang báo. Agent là nguồn sự thật; hub rớt
  hay khởi động lại thì các máy tự nối lại và gửi lại toàn bộ.

## Cài hub

Hub là ứng dụng ASP.NET Core thuần (`src/Cowork.Hub`), **không** dính WPF nên chạy được trên Linux —
một VPS nhỏ là đủ.

```bash
dotnet publish src/Cowork.Hub -c Release -o /opt/cowork-hub
```

Sửa `/opt/cowork-hub/appsettings.json`:

```json
{
  "Web":    { "Password": "mật khẩu dài để đăng nhập web" },
  "Agents": [
    { "Name": "may-nha",     "Token": "e3f1…(≥16 ký tự, mỗi máy một token riêng)" },
    { "Name": "may-cong-ty", "Token": "9b2c…" }
  ]
}
```

Hub **từ chối chạy** khi mật khẩu còn là giá trị mẫu, ngắn hơn 8 ký tự, hay token còn là mẫu.

Mảng `Agents` ở đây chỉ là **giống ban đầu**, dùng cho lần chạy đầu tiên. Từ lần đó trở đi, danh
sách máy nằm ở `App_Data/agents.json` bên cạnh hub và **file này thắng** — thêm hay bớt máy làm
trên web (xem *Cấp token* bên dưới), không sửa `appsettings.json` nữa.

Đặt `AgentStorePath` trong cấu hình để đổi chỗ lưu, chẳng hạn ra ngoài thư mục deploy.

Chạy sau một reverse proxy có HTTPS. Ví dụ với Caddy, `Caddyfile`:

```
hub.example.com {
    reverse_proxy 127.0.0.1:5080
}
```

```bash
cd /opt/cowork-hub && dotnet Cowork.Hub.dll --urls http://127.0.0.1:5080
```

Caddy tự lấy chứng chỉ Let's Encrypt và chuyển tiếp WebSocket. Để chạy bền, bọc lệnh trên trong một
unit systemd (hoặc Task Scheduler nếu hub đặt trên Windows).

## Nối máy vào hub

Trên từng máy, tab **Thiết lập → Quản lý từ xa**:

| Ô | Giá trị |
|---|---|
| Địa chỉ hub | `https://hub.example.com` — chỉ gốc, không thêm `/hubs/agent` |
| Mã agent | token của đúng máy này trong `appsettings.json` |

Bấm **Kết nối lại** (hoặc mở lại Cowork). Dòng *Trạng thái* chuyển sang *Đã kết nối*; mất mạng thì
nó tự thử lại mãi, cách nhau tối đa 30 giây, không cần ai bấm gì.

Tên máy trên web lấy từ **token**, không lấy từ tên máy tự khai — một agent cầm token của máy A không
thể tự nhận mình là máy B.

## Trên web

Đăng nhập bằng mật khẩu trong `Web:Password`. Trang chính liệt kê mọi máy đã cấp token, kể cả máy
chưa từng nối (để biết máy nào đang thiếu). Mỗi máy: app, trạng thái, chạy lần cuối, mốc kế tiếp, và
ba nút **Chạy / Dừng / Khởi động lại** — chỉ bấm được khi máy đang trực tuyến.

Lệnh từ web đi xuống agent, agent trả lời trong 15 giây; kết quả hiện ngay trên trang. Trên máy đó,
lịch sử ghi nguồn kích hoạt là **Từ xa**. Trạng thái cập nhật tức thời khi app đổi trạng thái, và
toàn bộ được gửi lại mỗi 30 giây (làm nhịp tim, đồng thời cập nhật mốc *Kế tiếp*).

Nút góc trên đổi ngôn ngữ cho riêng phiên trình duyệt đó. Riêng cột *Lịch* và câu trả lời của agent
là chữ agent gửi lên, theo ngôn ngữ đang đặt trên máy agent.

### Cấp token

Tab **Cấp token** (`/agents`) là nơi thêm và thu hồi máy. Nhập tên máy, lấy chuỗi ở ô token — đã
điền sẵn một chuỗi ngẫu nhiên, bấm *Sinh token* để đổi cái khác — rồi bấm *Thêm máy*. Dán token vừa
cấp vào ô **Mã agent (token)** trong Thiết lập của Cowork trên máy đó.

Token có hiệu lực **ngay**, không phải khởi động lại hub: sổ agent sửa được lúc chạy chứ không còn
dựng một lần lúc mở cổng. *Thu hồi* cũng vậy — máy bị thu hồi không nối lại được, và kết nối đang
treo của nó thôi báo trạng thái lên ngay lập tức.

Danh sách được ghi xuống `App_Data/agents.json` trước khi giao diện báo thành công. Ghi hỏng thì
thay đổi bị hoàn tác và trang báo lỗi kèm lời của hệ điều hành — để không có chuyện web hiện một
máy mà lần khởi động sau nó biến mất.

Khi deploy bằng WebDeploy nhớ **chừa `App_Data` ra** (`-skip:Directory="App_Data"`): `-verb:sync`
xoá mọi thứ không có trong thư mục nguồn, không skip là mất sạch máy đã cấp token.

## Bảo mật — đọc trước khi mở ra internet

Một trang web ra lệnh chạy chương trình trên nhiều máy **về bản chất là công cụ thực thi từ xa**. Tài
khoản web bị lộ nghĩa là kẻ khác chạy được mọi app đã khai trên mọi máy của bạn.

- **HTTPS bắt buộc.** Token agent và cookie đăng nhập đi qua đường này; HTTP trần là lộ hết.
- **Mật khẩu web dài**, và token **riêng cho từng máy** — thu hồi một máy chỉ cần bấm *Thu hồi*.
- Web **chỉ kích hoạt app đã khai sẵn** trên máy. Không sửa được đường dẫn, tham số, lịch hay file
  cấu hình từ xa — đây là chủ ý, để tài khoản web bị lộ vẫn không biến thành "chạy bất kỳ thứ gì".
- Token được lưu **dạng thường** trong `workspace.json` của từng máy, ngang mức các file cấu hình
  khác trong `%APPDATA%`. Máy dùng chung tài khoản Windows thì cân nhắc.
- Không có port nào được mở trên các máy chạy Cowork. Chỉ hub cần mở cổng ra ngoài.
- *Chạy với quyền quản trị* từ xa gần như vô dụng: UAC cần một người bấm Yes trên đúng máy đó.

## Giới hạn của bản này

- Chỉ **xem + chạy/dừng/khởi động lại**. Sửa config, lịch, thêm app vẫn làm trên máy đó.
- Không xem output từ xa; nhật ký vẫn nằm trong `%APPDATA%\Cowork\logs` của từng máy.
- Hub không lưu lịch sử. Máy rớt mạng hiện *ngoại tuyến* kèm ảnh chụp cuối và mốc "lần cuối thấy".
- Một tài khoản web duy nhất, một mật khẩu; chưa phân quyền theo máy.
- Kiến trúc test được đến đâu: `MachineRegistry`, `AgentDirectory`, hợp đồng dữ liệu có unit test; và
  một test tích hợp dựng hub thật trong tiến trình rồi nối bằng chính `HubClient` của Cowork
  (`HubIntegrationTests`). Phần giao diện web được kiểm bằng tay qua HTTP.
