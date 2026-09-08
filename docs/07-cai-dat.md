# 07 — Cài đặt

## Cài gì được ở đâu

| | Windows | Linux |
|---|---|---|
| **Cowork** (app desktop) | có | **không** |
| **Cowork Hub** (quản lý từ xa) | chạy được, chưa đóng gói | có |

App desktop viết bằng WPF (`net8.0-windows`, `UseWPF`) nên nó chỉ chạy trên Windows — đây là ràng
buộc của bộ khung giao diện, không phải một hạng mục còn thiếu. Trên Linux cài được hub, tức là phần
nhận kết nối từ các máy Windows và hiện chúng trên web.

## Windows — app Cowork

```powershell
winget install mtai0524.Cowork
```

> **Lệnh trên chưa chạy được.** Phát hành ở repo này không đưa gói vào kho winget — đó là một pull
> request riêng gửi [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) và Microsoft
> duyệt. Trong lúc chờ, dùng cách tải thẳng `.msi` ở mục dưới. Manifest đã sẵn sàng, sinh bằng
> `packaging/winget/generate-manifests.ps1`.

Gói cài **theo người dùng**, không hỏi UAC. Nó đặt:

| | |
|---|---|
| `%LOCALAPPDATA%\Programs\Cowork\` | chương trình |
| Menu Start → *Cowork* | lối tắt |
| `PATH` của người dùng | thêm thư mục trên, để gõ `cowork` ở terminal là mở được app |
| `%APPDATA%\Cowork\` | dữ liệu — **gỡ app không xoá thư mục này** |

Bản đóng gói là **self-contained**: máy không cần cài sẵn .NET runtime. Đổi lại file cài nặng
khoảng 54 MB.

Nâng cấp và gỡ:

```powershell
winget upgrade mtai0524.Cowork
winget uninstall mtai0524.Cowork
```

Muốn giữ lại danh sách app và lịch sử sau khi gỡ thì không cần làm gì — chúng nằm ở
`%APPDATA%\Cowork`, gói cài không đụng tới. Xoá hẳn thì xoá tay thư mục đó.

### Không dùng winget

Tải file `.msi` ở [trang phát hành](https://github.com/mtai0524/cowork-launcher/releases) rồi:

```powershell
msiexec /i Cowork-1.0.0-win-x64.msi          # có giao diện
msiexec /i Cowork-1.0.0-win-x64.msi /qn      # im lặng
msiexec /x Cowork-1.0.0-win-x64.msi /qn      # gỡ
```

## Linux — Cowork Hub

Debian, Ubuntu và các bản dẫn xuất (amd64):

```bash
curl -LO https://github.com/mtai0524/cowork-launcher/releases/download/v1.0.0/cowork-hub_1.0.0_amd64.deb
sudo apt install ./cowork-hub_1.0.0_amd64.deb
```

Dùng `apt install ./file.deb` chứ đừng `dpkg -i`: gói phụ thuộc vào ICU và OpenSSL, `apt` tự kéo về
còn `dpkg` thì dừng lại bắt bạn tự gỡ rối.

Cài xong **hub chưa chạy** — đó là chủ ý. Hub từ chối khởi động khi mật khẩu web còn là giá trị mẫu
(xem `HubOptions.Validate`), nên bật sẵn chỉ tạo ra một dịch vụ chết trong log. Còn hai bước:

```bash
sudo nano /etc/cowork-hub/cowork-hub.env     # đặt Web__Password và token cho từng máy
sudo systemctl enable --now cowork-hub
```

Kiểm tra:

```bash
systemctl status cowork-hub
journalctl -u cowork-hub -f
curl -I http://127.0.0.1:5099/login
```

### Gói đặt những gì

| Đường dẫn | Nội dung |
|---|---|
| `/opt/cowork-hub/` | chương trình (self-contained, không cần .NET runtime) |
| `/usr/bin/cowork-hub` | chạy tay ở tiền cảnh, tiện lúc dò lỗi |
| `/etc/cowork-hub/cowork-hub.env` | mật khẩu web và token — `0640`, chủ `root:cowork-hub` |
| `/lib/systemd/system/cowork-hub.service` | dịch vụ |
| `/var/lib/cowork-hub/agents.json` | sổ máy đã cấp token, systemd tạo qua `StateDirectory` |

Dịch vụ chạy bằng người dùng hệ thống `cowork-hub`, không phải root, với `ProtectSystem=strict` —
nó chỉ ghi được đúng `/var/lib/cowork-hub`.

### Cấu hình đi qua biến môi trường, không qua appsettings.json

`/etc/cowork-hub/cowork-hub.env` là file biến môi trường của systemd:

```ini
Web__Password=mat-khau-that-cua-ban
Agents__0__Name=may-nha
Agents__0__Token=token-dai-ngau-nhien
ASPNETCORE_URLS=http://127.0.0.1:5099
AgentStorePath=/var/lib/cowork-hub/agents.json
```

Gạch dưới đôi `__` là cách ASP.NET viết cấu hình lồng nhau, nên `Web__Password` chính là mục
`Web:Password`. ASP.NET nạp biến môi trường **sau** `appsettings.json` nên chúng đè lên giá trị mẫu
đóng trong gói — không phải sửa file nào bên trong `/opt`.

File này được khai là `conffile`, nên nâng cấp gói **không** ghi đè mật khẩu bạn đã đặt.

### Gỡ

```bash
sudo apt remove cowork-hub    # bỏ chương trình, GIỮ mật khẩu và sổ máy
sudo apt purge  cowork-hub    # xoá cả cấu hình, sổ máy và người dùng hệ thống
```

### Hub nghe ở đâu

Mặc định `http://127.0.0.1:5099` — chỉ localhost. Hub nhận mật khẩu qua form nên đừng mở thẳng ra
internet: đặt nginx hoặc Caddy phía trước để có HTTPS. Chi tiết trong
[06-quan-ly-tu-xa.md](06-quan-ly-tu-xa.md).

Đổi cổng thì sửa `ASPNETCORE_URLS` trong file env rồi `sudo systemctl restart cowork-hub`.

### Distro khác

Gói `.deb` chỉ dùng được cho họ Debian. Trên distro khác, cách chắc chắn nhất là dựng từ nguồn:

```bash
git clone https://github.com/mtai0524/cowork-launcher
cd cowork-launcher
dotnet publish src/Cowork.Hub -c Release -r linux-x64 --self-contained true -o /opt/cowork-hub
```

rồi chép `packaging/linux/cowork-hub.service` và `cowork-hub.env` vào đúng chỗ. Cần `libicu` và
`libssl` trên máy.

## Tự dựng gói

```powershell
pwsh packaging/windows/build-msi.ps1                       # -> artifacts/release/*.msi
pwsh packaging/winget/generate-manifests.ps1 -Msi <file>   # -> artifacts/winget/manifests/...
```

```bash
bash packaging/linux/build-deb.sh                          # -> artifacts/release/*.deb
```

Cần WiX v5 cho MSI (`dotnet tool install --global wix --version 5.0.2`) và `dpkg-deb` cho `.deb`.
Trên Windows có thể dựng `.deb` bằng WSL: publish trước rồi chạy
`wsl bash packaging/linux/build-deb.sh --skip-publish`.

## Phát hành một bản mới

1. Sửa `<Version>` trong [`Directory.Build.props`](../Directory.Build.props) — đó là nguồn duy nhất,
   cả hai gói và manifest winget đều đọc từ đó.
2. Commit, rồi gắn tag khớp:

   ```bash
   git tag v1.1.0 && git push origin v1.1.0
   ```

3. Workflow [`release.yml`](../.github/workflows/release.yml) chạy test, dựng MSI trên Windows và
   `.deb` trên Ubuntu (có cài thử rồi gỡ), rồi tạo bản phát hành kèm cả hai file và `SHA256SUMS`.
   Nó dừng ngay nếu tag không khớp `<Version>`.
4. Gửi manifest winget: chép `artifacts/winget/manifests/...` vào một nhánh của
   [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) rồi mở pull request. Microsoft
   duyệt tự động phần lớn, thường mất vài ngày.

`ProductCode` trong manifest **phải** là của đúng file `.msi` đã phát hành — WiX sinh mã mới cho mỗi
lần dựng. Đó là lý do manifest được sinh từ chính file `.msi` chứ không viết tay; chép lại mã cũ thì
`winget upgrade` và `winget uninstall` sẽ không nhận ra bản đã cài.
