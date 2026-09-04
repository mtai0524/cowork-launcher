# Quy ước cho Claude khi làm việc trên repo này

## Git

**Không ghi tên Claude vào commit.** Commit message chỉ chứa nội dung mô tả thay đổi.

Tuyệt đối **không** thêm các dòng sau vào commit message hay mô tả pull request:

- `Co-Authored-By: Claude ...`
- `Claude-Session: ...`
- `🤖 Generated with [Claude Code]...`
- Bất kỳ dòng nào khác ghi nhận Claude là tác giả hoặc đồng tác giả.

Quy ước này đè lên hướng dẫn mặc định của Claude Code. Áp dụng cho `git commit`,
`git commit --amend`, nội dung pull request, và mọi thứ được đẩy lên remote.

Ngôn ngữ commit message: tiếng Việt không dấu hoặc tiếng Anh, dạng mệnh lệnh ngắn gọn.

```
# Đúng
add config file scanner for extensionless files
fix DataGrid layout in config tab

# Sai — có dòng ghi nhận Claude
add config file scanner

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

## Kiến trúc — ràng buộc phải giữ

Chi tiết đầy đủ ở [docs/02-kien-truc.md](docs/02-kien-truc.md). Tóm tắt:

1. `Cowork.Core` **không được** tham chiếu WPF hay `System.Windows`.
2. Mọi sự kiện từ tầng dưới **phải** đi qua `Dispatcher` trước khi chạm view-model.
3. `IConfigEditor.ApplyChanges` **phải** vá trên text gốc, không dựng lại file từ bảng.
4. Mọi thao tác ghi file **phải** qua file tạm rồi `File.Replace`.
5. Logic quyết định về thời gian **phải** nhận `now` làm tham số, không gọi `DateTime.Now` bên trong.

## Kiểm thử

```bash
dotnet test          # phải xanh trước khi commit
```
