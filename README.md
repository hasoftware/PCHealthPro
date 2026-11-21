# PC Health Pro

Ứng dụng Windows hỗ trợ kiểm tra máy tính trước khi mua hoặc bàn giao. Bản phát triển v0.0.1 đọc thông tin máy, BIOS, mainboard, CPU, từng thanh RAM, GPU, màn hình, mạng và thiết bị ngoại vi. Giao diện WinUI 3 hỗ trợ tiếng Việt và tiếng Anh.

## Build và chạy bản phát triển

Dùng Windows x64 với .NET SDK 10 và Windows SDK. Môi trường đã thử dùng .NET SDK 10.0.104 và có Windows SDK 10.0.19041 cùng 10.0.26100. Chạy các lệnh sau từ thư mục chứa solution. Lần restore đầu tiên cần truy cập NuGet nếu máy chưa có đủ package.

```powershell
dotnet restore PCHealthPro.sln
dotnet build PCHealthPro.sln --no-restore
dotnet test tests/PCHealthPro.Tests --no-build
dotnet run --project src/PCHealthPro.App --no-build
```

Chọn Tổng quan để quét máy. Mở các nhóm trong từng thẻ để xem chi tiết. Thẻ Windows hiển thị thông tin kích hoạt do Windows cung cấp và dữ liệu Office khi có. KMS cũng được sử dụng trong các tổ chức có giấy phép. Trạng thái kích hoạt cần được đối chiếu với chứng từ nếu cần xác minh nguồn bản quyền.

Ứng dụng đọc cục bộ bằng quyền người dùng hiện tại. Trường thiếu quyền hoặc thiếu driver hiển thị “Không có dữ liệu”. Lỗi truy vấn được ghi theo tên nguồn và loại lỗi trong thư mục `logs` cạnh ứng dụng. Mở Cài đặt để đổi ngôn ngữ. Ứng dụng lưu lựa chọn ngôn ngữ và trạng thái hướng dẫn lần đầu tại `%LOCALAPPDATA%\HASOFTWARE\PCHealthPro`.

## Mã nguồn

- `PCHealthPro.Core` định nghĩa giao diện module và điều phối kiểm tra.
- `PCHealthPro.App` chứa cửa sổ WinUI 3, tài nguyên ngôn ngữ và cấu hình DI.
- `PCHealthPro.Tests` kiểm tra xử lý lỗi module và hủy kiểm tra.

## Tác giả

Dự án được phát triển bởi HASOFTWARE do Dev Hoang Anh Dev thực hiện. Website [hasoftware.vn](https://hasoftware.vn). Telegram Dev [@HoangAnhDev](https://t.me/HoangAnhDev). Telegram Channel [@hasoftware](https://t.me/hasoftware).

