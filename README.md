# Construction Project Management System

Hệ thống quản lý dự án thi công xây dựng bằng ASP.NET Core MVC .NET 10, Entity Framework Core,
SQL Server và ASP.NET Core Identity.

## Trạng thái

Giai đoạn hiện tại: **Giai đoạn 2 — Tạo khung dự án và tài liệu thiết kế**.

Dự án mới được tạo độc lập tại `D:\MYPROJECT\ConstructionProjectManagement`. Phạm vi triển khai
chỉ gồm 12 nghiệp vụ và 6 quy trình được mô tả trong Rule.md.

## Chạy dự án

```powershell
dotnet restore
dotnet build
dotnet run
```

Mặc định sử dụng SQLite với connection string trong `appsettings.json`. Có thể thay đổi connection string theo
môi trường triển khai; không commit thông tin xác thực nhạy cảm.

### Migration TASK-06

Ứng dụng không khởi động nếu migration `AddTask06TrackingExtensions` còn chờ và chưa bật
`CPMS_ALLOW_TASK06_MIGRATION=true`. Trước khi bật, xác nhận connection string, sao lưu database và kiểm tra trên
bản sao: ngày kết thúc kế hoạch không trước ngày bắt đầu, phần trăm trong khoảng 0–100, không có phụ thuộc
tự tham chiếu hoặc trùng cặp. Migration tạo lại một số bảng SQLite và sẽ thất bại nếu dữ liệu vi phạm ràng buộc;
không tự sửa dữ liệu cũ. Sau khi xác minh và được phép triển khai, đặt biến môi trường cho lần khởi động cần
migrate. Không bật biến này cho database chính khi chạy kiểm thử.

### Database giả lập TASK-06

Công cụ `tests\Task06FakeData` chỉ tạo database **mới**, có tên
`ConstructionProjectManagement.Task06FakeTest.db`; nó từ chối ghi đè database hoặc bản sao đã tồn tại.
Công cụ kiểm tra database rỗng, tạo và kiểm tra bản sao `.before-migration.bak`, sau đó mới áp dụng
migration với cờ cho phép. Không dùng công cụ này với database chính.

Tạo một bộ mới trong thư mục chưa sử dụng:

```powershell
Set-Location 'D:\Task 3\Task-2-main'
$fakeDb = 'D:\Task 3\task06-demo-moi\ConstructionProjectManagement.Task06FakeTest.db'
$env:TASK06_FAKE_PASSWORD = 'Demo06Test!' # chỉ dành cho tài khoản giả lập
$env:CPMS_ALLOW_TASK06_MIGRATION = 'true'
try {
    dotnet run --project 'tests\Task06FakeData\Task06FakeData.csproj' -- $fakeDb
    if ($LASTEXITCODE -ne 0) { throw 'Không tạo được database giả lập.' }
}
finally {
    Remove-Item Env:\CPMS_ALLOW_TASK06_MIGRATION, Env:\TASK06_FAKE_PASSWORD -ErrorAction SilentlyContinue
}
```

Sau khi tạo thành công, khởi chạy ứng dụng trỏ tới chính file giả lập. Không bật migration trong lệnh này:

```powershell
Set-Location 'D:\Task 3\Task-2-main'
$env:ConnectionStrings__DefaultConnection = 'Data Source=D:\Task 3\task06-demo-moi\ConstructionProjectManagement.Task06FakeTest.db;Pooling=False'
$env:CPMS_ALLOW_TASK06_MIGRATION = 'false'
$env:CPMS_SEED_SAMPLE_DATA = 'false'
$env:CPMS_SEED_TASK02_TEST_DATA = 'false'
$env:CPMS_REMOVE_TASK02_TEST_DATA = 'false'
$env:CPMS_SEED_TASK03_TEST_DATA = 'false'
Remove-Item Env:\CPMS_ADMIN_EMAIL, Env:\CPMS_ADMIN_PASSWORD -ErrorAction SilentlyContinue
dotnet run --project 'ConstructionProjectManagement.csproj' --no-launch-profile --urls 'http://127.0.0.1:51867'
```

Truy cập `http://127.0.0.1:51867/Progress`. Công cụ in danh sách tài khoản; mật khẩu là
giá trị `TASK06_FAKE_PASSWORD` đã đặt lúc tạo. Sau khi dừng ứng dụng, xóa biến
`ConnectionStrings__DefaultConnection` khỏi phiên PowerShell trước khi chạy ứng dụng ở môi trường khác.

## Tài liệu

- [Kế hoạch dự án ban đầu](docs/05_Project_Management/Initial_Project_Plan.md)
- [Tài liệu sơ đồ](docs/06_Diagrams/README_Diagrams.md)
- [Rule.md](../BuildManagerMVC/Rule.md)
