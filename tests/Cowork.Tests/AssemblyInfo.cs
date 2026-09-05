// Ngôn ngữ giao diện là trạng thái tĩnh toàn cục (Cowork chỉ có một giao diện tại
// một thời điểm). LocalizationTests phải đổi trạng thái đó, mà các bài khác lại
// khẳng định trên chuỗi tiếng Việt, nên chạy song song sẽ sinh lỗi chập chờn.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
