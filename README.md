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

## Benchmark ổ cứng

Mở trang Ổ cứng, chọn phân vùng và kích thước file thử (256 MiB, 1 GiB hoặc 4 GiB). Nếu thư mục mặc định thiếu quyền ghi, chọn một thư mục khác trên phân vùng muốn đo. Nút **Bắt đầu đo** hiển thị vị trí, kích thước file và cảnh báo hao mòn SSD trước khi chạy.

Ứng dụng ghi rồi đọc tuần tự toàn bộ file theo khối 1 MiB. Sau đó ứng dụng đo ghi và đọc ngẫu nhiên khối 4 KiB, lần lượt với một và tối đa 32 yêu cầu I/O đồng thời (QD1/QD32). Mỗi lượt ngẫu nhiên kết thúc khi đạt kích thước file hoặc sau khoảng ba giây, cộng thời gian hoàn tất I/O đang chờ. Biểu đồ hiển thị tốc độ theo thời gian của lượt hiện tại. Kết quả mỗi lượt gồm tốc độ trung bình tính theo MB/s thập phân và số thao tác mỗi giây (IOPS).

Phép đo dùng I/O trực tiếp và write-through, kiểm tra dung lượng trống, giữ 16 MiB dự phòng và tạo một file mới với chế độ xóa khi đóng. Nút **Hủy** dừng phép đo và giữ kết quả những lượt đã hoàn tất.

## Kiểm tra tương tác

Trên trang Camera, chọn thiết bị rồi mở xem trực tiếp. Ứng dụng hiển thị kích thước và tốc độ khung hình của định dạng camera đang dùng. Chọn **Chụp ảnh thử** để giữ một khung hình bên dưới luồng xem trước.

Trong **Âm thanh & Mic**, chọn micro để xem mức tín hiệu hoặc thu thử 5 giây. Nếu có đầu ra, ứng dụng tự phát lại bằng loa đã chọn. Phần Loa hỗ trợ thử kênh trái, phải, cả hai và quét tần số từ 80 Hz đến 12 kHz. Thanh âm lượng chỉ tác động lên âm thanh thử của ứng dụng. Camera và micro sử dụng quyền dành cho ứng dụng desktop trong cài đặt quyền riêng tư Windows. Rời trang sẽ giải phóng thiết bị và bỏ nội dung thu thử khỏi phiên.

Trên trang Màn hình, mở chế độ toàn màn hình, nhấp chuột hoặc nhấn phím để chuyển qua năm màu và một dải chuyển sắc. Nhấn Esc để thoát. Trên trang Bàn phím & Touchpad, chọn **Bắt đầu thử phím** để xem các phím Windows chuyển đến ứng dụng. Trong vùng vẽ, thử chuột trái, chuột phải, cuộn hoặc chạm bằng nhiều ngón. Bộ đếm điểm chạm phản ánh các tiếp xúc Windows gửi dưới dạng cảm ứng; cử chỉ touchpad có thể xuất hiện dưới dạng chuột hoặc cuộn.

Chọn **Đạt**, **Không đạt** hoặc **Bỏ qua** riêng cho từng bài. Trang **Báo cáo** tổng hợp sáu kết quả của phiên đang mở. Ảnh thử và âm thanh được xử lý trong bộ nhớ.

## Xuất báo cáo và so sánh các lần kiểm tra

Mở **Tổng quan**, đợi ứng dụng quét xong rồi dùng **Kiểm tra tiếp** để thực hiện các bài tương tác còn lại. Trên trang **Báo cáo**, chọn **Phiên hiện tại**, nhập người bán, giá kèm đơn vị tiền và ghi chú ngoại hình nếu cần.

- Chọn **PDF** để in hoặc gửi bản đã dàn trang, **HTML** để đọc bằng trình duyệt ngoại tuyến hoặc **JSON** để xử lý dữ liệu. Chọn **Xuất file** và chọn nơi lưu. Báo cáo gồm thời điểm quét, tên máy, thông số, đánh giá, sáu kết quả tương tác và các lượt benchmark đã đo trong phiên.
- Tùy chọn **Ẩn số sê-ri và địa chỉ MAC** được bật sẵn và áp dụng cả khi xuất file lẫn lưu lịch sử. Bản đã lưu với tùy chọn này sẽ giữ các giá trị đã che. Tên máy và ghi chú vẫn xuất hiện trong báo cáo, nên hãy xem lại trước khi chia sẻ. Ảnh và âm thanh thử chỉ tồn tại trong bộ nhớ.
- Chọn **Lưu lịch sử** để lưu một bản JSON tại `%LOCALAPPDATA%\HASOFTWARE\PCHealthPro\history`. Mở lại trang **Báo cáo** và chọn lần kiểm tra theo tên máy, ngày giờ để xem hoặc xuất lại. Ở ô **So sánh**, chọn bản đối chiếu. Mỗi thay đổi hiển thị theo chiều từ bản đối chiếu sang bản đang xem. Có thể sao lưu các file JSON trong thư mục này.

## Đóng gói và chạy trên Windows x64

Từ thư mục chứa solution, chạy lệnh sau trên máy build. Script dùng .NET SDK 10, khôi phục WiX từ manifest của kho mã và đóng gói cùng một bản ứng dụng cho cả hai hình thức phân phối.

```powershell
.\scripts\publish.ps1
```

Thư mục `artifacts` chứa ZIP portable, bộ cài MSI và file `.sha256` theo phiên bản khai báo trong `Directory.Build.props`. Cả hai gói đều chứa runtime .NET và Windows App SDK. Máy sử dụng chỉ cần Windows x64 tương thích.

- **Portable**. Giải nén toàn bộ ZIP vào một thư mục trên máy hoặc USB rồi mở `PCHealthPro.App.exe`. Giữ các DLL và tài nguyên giao diện cùng cấu trúc thư mục đã giải nén. Lịch sử và lựa chọn ngôn ngữ thuộc tài khoản Windows trên máy đang dùng.
- **MSI**. Mở file MSI để cài cho tài khoản hiện tại. Khởi chạy **PC Health Pro** từ Start Menu. Gỡ qua mục ứng dụng đã cài của Windows. Lịch sử kiểm tra được lưu riêng với thư mục cài đặt.

Gói nghiệm thu v0.0.1 chưa ký số. Trước khi chạy gói phân phối, lấy bộ cài và danh sách SHA-256 từ nguồn HASOFTWARE tin cậy rồi đối chiếu kết quả của lệnh sau với dòng có cùng tên file.

```powershell
Get-FileHash .\artifacts\PCHealthPro-0.0.1-win-x64.zip -Algorithm SHA256
Get-FileHash .\artifacts\PCHealthPro-0.0.1-win-x64.msi -Algorithm SHA256
Get-Content .\artifacts\PCHealthPro-0.0.1-win-x64.sha256
```

Kiểm tra phần cứng và xuất báo cáo hoạt động ngoại tuyến. Trong **Cài đặt**, có thể bật kiểm tra GitHub Releases khi mở ứng dụng bằng quyền thường. Khi có bản mới, ứng dụng hỏi trước khi tải, kiểm tra kích thước và SHA-256, rồi mở MSI hoặc giải nén và chạy bản portable ở thư mục bên cạnh. Hãy lưu phiên kiểm tra trước khi đồng ý cập nhật. Dữ liệu kiểm tra được giữ trên máy. [Kho mã và bản phát hành của HASOFTWARE](https://github.com/hasoftware/PCHealthPro/releases)

## Mã nguồn

- `PCHealthPro.Core` định nghĩa giao diện module và điều phối kiểm tra.
- `PCHealthPro.App` chứa cửa sổ WinUI 3, tài nguyên ngôn ngữ và cấu hình DI.
- `PCHealthPro.Tests` kiểm tra logic chẩn đoán, benchmark, kết quả tương tác, tạo báo cáo, ẩn định danh và lịch sử.
- `installer/Package.wxs` mô tả bộ cài MSI. `scripts/publish.ps1` tạo các gói và tính SHA-256.

## Tác giả

Dự án được phát triển bởi HASOFTWARE do Dev Hoang Anh Dev thực hiện. Website [hasoftware.vn](https://hasoftware.vn). Telegram Dev [@HoangAnhDev](https://t.me/HoangAnhDev). Telegram Channel [@hasoftware](https://t.me/hasoftware).

