#!/usr/bin/env bash
#
# Dựng gói .deb cài Cowork Hub trên Debian/Ubuntu.
#
#     packaging/linux/build-deb.sh [--skip-publish]
#
# Kết quả: artifacts/release/cowork-hub_<ver>_amd64.deb
#
# `--skip-publish` dùng lại thư mục artifacts/deb-publish có sẵn — để dựng gói được trên
# một máy không có .NET SDK (ví dụ chạy dpkg-deb trong WSL sau khi đã publish bên Windows).
#
# Bản publish là SELF-CONTAINED, cùng lý do với gói Windows: không bắt máy đích cài sẵn
# .NET runtime. Đổi lại vẫn còn hai thư viện hệ thống thật sự cần — ICU cho phần định dạng
# theo ngôn ngữ và OpenSSL cho HTTPS ra ngoài (bảng tin gọi RSS) — nên chúng nằm ở Depends.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
publish_dir="$root/artifacts/deb-publish"
out_dir="$root/artifacts/release"

# Dựng ở hệ thống file của Linux, không dựng thẳng trong repo: khi repo nằm trên ổ Windows
# (WSL, /mnt/c) mọi thứ hiện ra với quyền 777, và dpkg-deb từ chối thư mục control như vậy.
stage_dir="$(mktemp -d)"
trap 'rm -rf "$stage_dir"' EXIT

skip_publish=0
[[ "${1:-}" == "--skip-publish" ]] && skip_publish=1

# ---------- phiên bản ----------
if command -v dotnet >/dev/null 2>&1; then
    version="$(dotnet msbuild "$root/src/Cowork.Hub/Cowork.Hub.csproj" -getProperty:Version -nologo | tr -d '[:space:]')"
else
    # Không có SDK thì đọc thẳng từ Directory.Build.props.
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)"
fi
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]] || { echo "Không đọc được Version: '$version'" >&2; exit 1; }

echo "Cowork Hub $version -> amd64"

# ---------- publish ----------
if [[ $skip_publish -eq 0 ]]; then
    command -v dotnet >/dev/null 2>&1 || { echo "Cần .NET SDK, hoặc chạy với --skip-publish" >&2; exit 1; }
    rm -rf "$publish_dir"
    dotnet publish "$root/src/Cowork.Hub/Cowork.Hub.csproj" \
        -c Release -r linux-x64 --self-contained true \
        -p:DebugType=none -p:DebugSymbols=false \
        -o "$publish_dir"
fi
[[ -f "$publish_dir/Cowork.Hub" ]] || { echo "Thiếu $publish_dir/Cowork.Hub" >&2; exit 1; }

# ---------- dựng cây thư mục ----------
chmod 0755 "$stage_dir"
mkdir -p "$stage_dir/DEBIAN" \
         "$stage_dir/opt/cowork-hub" \
         "$stage_dir/usr/bin" \
         "$stage_dir/etc/cowork-hub" \
         "$stage_dir/lib/systemd/system" \
         "$stage_dir/usr/share/doc/cowork-hub" \
         "$out_dir"

cp -a "$publish_dir/." "$stage_dir/opt/cowork-hub/"
chmod 0755 "$stage_dir/opt/cowork-hub/Cowork.Hub"

cp "$root/packaging/linux/cowork-hub.service" "$stage_dir/lib/systemd/system/cowork-hub.service"
cp "$root/packaging/linux/cowork-hub.env" "$stage_dir/etc/cowork-hub/cowork-hub.env"
chmod 0600 "$stage_dir/etc/cowork-hub/cowork-hub.env"

# Chạy tay ở tiền cảnh, tiện để xem log lúc dò lỗi. Đọc cùng file cấu hình với service;
# người dùng thường không đọc được file 0600 đó, khi ấy hub báo thiếu mật khẩu rồi dừng —
# đúng hành vi mong muốn, không phải chạy lén bằng cấu hình mẫu.
cat > "$stage_dir/usr/bin/cowork-hub" <<'WRAPPER'
#!/bin/sh
set -e
if [ -r /etc/cowork-hub/cowork-hub.env ]; then
    set -a
    . /etc/cowork-hub/cowork-hub.env
    set +a
fi
cd /opt/cowork-hub
exec ./Cowork.Hub "$@"
WRAPPER
chmod 0755 "$stage_dir/usr/bin/cowork-hub"

cp "$root/README.md" "$stage_dir/usr/share/doc/cowork-hub/README.md"

# ---------- siêu dữ liệu ----------
installed_kb="$(du -sk "$stage_dir" | cut -f1)"

cat > "$stage_dir/DEBIAN/control" <<CONTROL
Package: cowork-hub
Version: $version
Section: web
Priority: optional
Architecture: amd64
Maintainer: mtai0524 <mtai0524@users.noreply.github.com>
Installed-Size: $installed_kb
Depends: adduser, libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70, libssl3t64 | libssl3
Homepage: https://github.com/mtai0524/cowork-launcher
Description: Hub quan ly tu xa cho Cowork
 Cowork Hub nhan ket noi tu cac may chay Cowork, hien trang thai tren web va
 chuyen lenh Chay / Dung / Khoi dong lai xuong tung may. Kem mot bang tin doc
 RSS theo chu de.
 .
 Ban dong goi la self-contained: khong can cai san .NET runtime.
 .
 Sau khi cai, sua /etc/cowork-hub/cowork-hub.env roi bat dich vu bang
 systemctl enable --now cowork-hub
CONTROL

# conffiles: dpkg không đè file người dùng đã sửa khi nâng cấp. Thiếu dòng này thì mọi
# lần nâng cấp sẽ ghi mật khẩu mẫu đè lên mật khẩu thật.
echo "/etc/cowork-hub/cowork-hub.env" > "$stage_dir/DEBIAN/conffiles"

cp "$root/packaging/linux/postinst" "$stage_dir/DEBIAN/postinst"
cp "$root/packaging/linux/prerm" "$stage_dir/DEBIAN/prerm"
cp "$root/packaging/linux/postrm" "$stage_dir/DEBIAN/postrm"
chmod 0755 "$stage_dir/DEBIAN/postinst" "$stage_dir/DEBIAN/prerm" "$stage_dir/DEBIAN/postrm"

# ---------- đóng gói ----------
deb="$out_dir/cowork-hub_${version}_amd64.deb"
rm -f "$deb"

# dpkg-deb đòi mọi thứ thuộc root; chạy dưới người dùng thường thì --root-owner-group lo phần đó.
dpkg-deb --root-owner-group --build "$stage_dir" "$deb" >/dev/null

echo
echo "deb    : $deb ($(du -h "$deb" | cut -f1))"
echo "sha256 : $(sha256sum "$deb" | cut -d' ' -f1)"
echo
echo "Cài  : sudo apt install ./$(basename "$deb")"
echo "Gỡ   : sudo apt remove cowork-hub      (giữ cấu hình)"
echo "Xoá  : sudo apt purge cowork-hub       (xoá cả cấu hình và sổ máy)"
