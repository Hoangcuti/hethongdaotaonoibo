# TỔNG HỢP TOÀN BỘ CÔNG VIỆC ĐÃ HOÀN THÀNH & LỖI ĐÃ SỬA (TỪ TRƯỚC TỚI NAY)

Tài liệu này tổng hợp toàn bộ các tính năng đã cập nhật, phát triển mới và danh sách các lỗi đã được sửa đổi trong hệ thống Quản lý Đào tạo Nội bộ (LMS).

---

## 1. DANH SÁCH CÁC CHỨC NĂNG ĐÃ CẬP NHẬT & PHÁT TRIỂN

### 1.1. Luồng Phê duyệt & Đề xuất Học liệu Nâng cao
* **Tương tác trực tiếp trên thông báo:** Nâng cấp hệ thống thông báo đề xuất tài liệu. Thay vì nút "Đã đọc", người duyệt (Phòng Đào tạo) có các nút bấm thao tác trực tiếp: **"Xem chi tiết"**, **"Xác nhận (Phê duyệt)"**, **"Từ chối"** ngay trên giao diện thông báo.
* **Quản lý lý do từ chối:** Khi từ chối đề xuất học liệu, Phòng Đào tạo bắt buộc phải nhập lý do từ chối. Hệ thống tự động gửi thông báo phản hồi có nhãn `[TỪ CHỐI]` kèm nút **"Xem lý do từ chối"** để người đề xuất (HR/Trưởng bộ phận) xem chi tiết qua modal.
* **Phân quyền Sidebar:** Cấu hình động menu Sidebar, chỉ hiển thị mục **Phê duyệt** (Approvals) cho tài khoản Manager thuộc phòng ban **"Trung tâm Đào tạo Nội bộ"** (Phòng Đào tạo).

### 1.2. Tính năng Quản lý Giáo trình & Tái sử dụng Học liệu
* **Xem lịch sử sử dụng học liệu:** Bổ sung tính năng hiển thị chi tiết các Chương học, Bài giảng, Quiz đang được sử dụng ở những Khóa học nào để người quản lý nắm thông tin.
* **Bộ lọc tái sử dụng học liệu:** Thêm bộ lọc cho phép tìm kiếm và tái sử dụng các chương học, bài giảng cũ khi tạo khóa học mới (phục vụ trường hợp đổi mã môn học hoặc dùng chung giáo trình).
* **Xóa tài liệu đính kèm:** Bổ sung tính năng xóa tài liệu đính kèm khỏi bài giảng nếu không còn sử dụng để giải phóng dung lượng hệ thống.

### 1.3. Hệ thống Thông báo & Quản lý Thời hạn Học tập
* **Thông báo quá hạn:** Hệ thống tự động gửi thông báo cho Trưởng phòng khi học viên dưới quyền học tập quá hạn (DueDate).
* **Gia hạn học tập:** 
  * Học viên có nút gửi yêu cầu **Xin gia hạn học tập** lên Trưởng phòng khi khóa học bị khóa do quá hạn.
  * Trưởng phòng có chức năng phê duyệt gia hạn thời gian học tập (cho phép học tiếp tối đa 3 ngày).

### 1.4. Tính năng Thi cử & Chứng chỉ
* **Chức năng bài thi (Quiz/Exam):** Tích hợp và hoàn thiện giao diện làm bài thi trắc nghiệm tính điểm cho học viên.
* **Cấp chứng chỉ điện tử:** Hệ thống tự động cấp chứng chỉ dạng ảnh/PDF cho học viên sau khi hoàn thành 100% tiến độ học tập và đạt điểm chuẩn tất cả các bài Quiz.

### 1.5. Tiện ích Trí tuệ nhân tạo (AI Gemini)
* **Tóm tắt nội dung bằng AI:** Học viên có thể nhấn nút **✨ AI** để tự động tạo bản tóm tắt nhanh nội dung chính của chương học.
* **Trợ lý Chatbot AI:** Tích hợp chatbot hỏi đáp thông minh ở góc màn hình để học viên trao đổi, giải nghĩa thuật ngữ bài học bất kỳ lúc nào.
* **Tạo Quiz tự động bằng AI:** Hỗ trợ người quản trị tạo nhanh các bộ câu hỏi trắc nghiệm tự động từ tài liệu học tập tải lên thông qua AI.

### 1.6. Giao diện & Tiện ích Trải nghiệm người dùng (UX/UI)
* **Trang giới thiệu hệ thống (Landing Page):** Thiết kế lại giao diện trang giới thiệu LMS (`Views/Home/Index.cshtml`) hiện đại, thẩm mỹ cao.
* **Giao diện tạo khóa học 3 bước:** Chuyển đổi quy trình tạo khóa học phức tạp thành quy trình 3 bước đơn giản, trực quan.
* **Trang Đăng nhập cải tiến:** 
  * Tích hợp nút toggle **Ẩn/Hiện mật khẩu** trên màn hình đăng nhập.
  * Cập nhật các icon và nhãn văn bản trực quan.
* **Đăng nhập nhanh (Quick Login):** 
  * Đổi nhãn tài khoản từ "Trưởng phòng HR" thành **"Trưởng phòng Đào tạo"**.
  * Chuyển hướng đăng nhập nhanh của HR về tài khoản quản lý phòng đào tạo `lanhhgv0001` (Hoàng Hương Lan).
  * Tự động cập nhật tên hiển thị của tài khoản này thành **"Phòng Đào tạo"** dưới database thông qua startup seeder.

### 1.7. Quản trị & Sao lưu hệ thống (IT System)
* Sao lưu cơ sở dữ liệu (Backup DB) cho phép tải trực tiếp file `.sql` sao lưu về thiết bị cá nhân để lưu trữ.

---

## 2. DANH SÁCH CÁC LỖI ĐÃ KHẮC PHỤC (BUG FIXES)

* **Lỗi ngắt kết nối Database trên Hosting:** Khắc phục lỗi tự động ngắt kết nối cơ sở dữ liệu khi hệ thống chạy trên hosting lâu không thao tác (tránh tình trạng phải F5 tải lại trang web để nối lại database khi đang demo).
* **Lỗi tải tài liệu của Học viên:** Khắc phục triệt để lỗi học viên không thể tải xuống các tài liệu đính kèm (PDF, Word,...) từ bài giảng.
* **Lỗi trùng lặp hàm JS sinh Quiz bằng AI:** Sửa lỗi trùng lặp khai báo các hàm Javascript gọi API AI trong file `wwwroot/js/it-dashboard.js`.
* **Lỗi chỉnh sửa câu hỏi Quiz:** Sửa lỗi hiển thị và lưu thông tin khi chỉnh sửa nội dung chi tiết của các bài Quiz có sẵn.
* **Lỗi hiển thị Logo:** Sửa lỗi hiển thị đường dẫn hình ảnh logo hệ thống bị lỗi hoặc không tải được.
* **Lỗi lệch giao diện Dashboard:** Sửa các lỗi lệch CSS, lệch nút bấm và hiển thị bảng biểu trên trang quản lý của HR/Manager (`dashboard.css`).
* **Tối ưu hóa Server Database:** Chuyển cấu hình kết nối sang SQL host mới hoạt động ổn định và có tốc độ phản hồi nhanh hơn.
