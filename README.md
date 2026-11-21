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

## Kiểm tra ổ cứng và pin

Trang Ổ cứng liệt kê từng thiết bị, giao tiếp và bộ đếm SMART mà driver cho phép đọc. Với NVMe, tổng dữ liệu ghi dùng TB thập phân. Với ATA, ứng dụng đọc trang thống kê chuẩn nếu ổ hỗ trợ và hiển thị các thuộc tính SMART có thể giải mã. Các bộ đếm khác nhau theo giao tiếp và firmware. Khi thiếu chỉ số, trang này có nút mở lại bằng quyền quản trị.

Mỗi ổ có đánh giá và lý do. Windows báo ổ lỗi, SMART cảnh báo nghiêm trọng, sector chờ xử lý, lỗi dữ liệu chưa sửa hoặc mức dùng tuổi thọ từ 100% sẽ hiện “Có vấn đề”. Các ngưỡng cảnh báo của ứng dụng gồm nhiệt độ từ 60 °C và tuổi thọ đã dùng từ 90%. Trang cũng cảnh báo khi ổ có sector đã phân bổ lại hoặc Windows Storage báo cần chú ý.

Trang Pin hiển thị dữ liệu riêng từng bộ pin. Độ chai được tính từ dung lượng thiết kế và dung lượng sạc đầy hiện tại. Nếu WMI thiếu thông tin, ứng dụng có thể đọc phần pin đã lắp trong báo cáo XML tạm thời của powercfg và xóa báo cáo sau khi đọc. Máy bàn có trạng thái riêng khi Windows xác nhận máy không có pin.

## Mã nguồn

- `PCHealthPro.Core` định nghĩa giao diện module và điều phối kiểm tra.
- `PCHealthPro.App` chứa cửa sổ WinUI 3, tài nguyên ngôn ngữ và cấu hình DI.
- `PCHealthPro.Tests` kiểm tra xử lý lỗi module và hủy kiểm tra.

## Tác giả

Dự án được phát triển bởi HASOFTWARE do Dev Hoang Anh Dev thực hiện. Website [hasoftware.vn](https://hasoftware.vn). Telegram Dev [@HoangAnhDev](https://t.me/HoangAnhDev). Telegram Channel [@hasoftware](https://t.me/hasoftware).

