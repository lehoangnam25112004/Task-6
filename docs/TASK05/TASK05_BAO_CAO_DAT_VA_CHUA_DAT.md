# Báo cáo hiện trạng TASK-05 — Quản lý công việc

> **Cập nhật Đợt 2:** Các mục 1–9 phía dưới là ảnh chụp trước Đợt 1, không phải kết luận mới nhất. Xem các cập nhật Đợt 2 và Đợt 1 trước khi sử dụng báo cáo.

## Cập nhật Đợt 2 — hiện trạng mới nhất

### Đã thực hiện

- **Completion:** [CompletionService](../../Services/CompletionService.cs) lọc công việc mở theo `TaskAccess` trước khi giới hạn 100 dòng; [CompletionController](../../Controllers/CompletionController.cs) nạp user và role Identity hiện tại, chặn tài khoản không hợp lệ. Admin có scope toàn hệ thống; PD/PM theo dự án; nếu có Client Representative thì phải có membership active và tổ chức khớp. Dự án chỉ đọc vẫn được xem theo scope. Theo lựa chọn của người dùng, **chỉ áp dụng scope cho danh sách công việc mở**; chi phí, tổng chi, bàn giao, dropdown và thao tác ghi/lưu trữ Completion giữ nghiệp vụ và quyền cũ, không được xem là đã bảo vệ scope theo TASK-05.
- **Danh sách Task:** lọc kết hợp theo dự án, trạng thái, ưu tiên và từ khóa tên/mô tả, không phân biệt hoa thường; chỉ lọc trong dữ liệu đã qua scope. Lựa chọn dự án chỉ hiển thị dự án trong scope, giữ giá trị bộ lọc khi tìm và từ chối giá trị enum không hợp lệ.
- **Giao diện:** nhãn trạng thái/ưu tiên Task, tiến độ, kết quả nghiệm thu và trạng thái/ưu tiên Quality được Việt hóa; giá trị enum lưu và gửi không đổi. Đã đồng bộ giới hạn tên 200 và mô tả 2000 ký tự trên ViewModel; hiển thị lỗi mô tả. Khi xóa lựa chọn dự án trên form tạo Task, trang cũng nạp lại để xóa các lựa chọn phụ thuộc cũ.
- **Fixture browser:** DB tạm dùng `Pooling=False`; chỉ xóa ba đường dẫn DB tạm đã tạo, thử lại hữu hạn nếu gặp `IOException` và tiếp tục báo lỗi nếu vẫn không xóa được. Không gọi `ClearAllPools` toàn cục.

### Kiểm thử

| Bộ kiểm thử | Kết quả cuối | Môi trường |
|---|---:|---|
| Unit/integration `ConstructionProjectManagement.Tests` | **120 pass / 0 fail / 0 skipped** | SQLite in-memory |
| UI `WbsUi.Tests` | **34 pass / 0 fail / 0 skipped** | Playwright, Microsoft Edge headless, SQLite file tạm |

Hai bộ chạy với `dotnet test` và `--no-restore`. Không đổi schema/migration hoặc sử dụng DB chính. Unit/integration bổ sung kiểm tra lọc Completion trước giới hạn 100, PD và Admin + Client Representative, user inactive, lọc Task theo nhiều tiêu chí, enum không hợp lệ, giới hạn độ dài. UI TASK-05 gồm 20 ca: hai test ban đầu, ma trận tám role cùng Admin+PM và Admin+PD, ba ca scope biên, hai ca Worker/SE không được giao và ba ca Completion Admin/PD/PM.

| Role | Task trong scope / ngoài scope | Nút sửa Task dự án ghi được | Ghi Progress khi được giao | Quality |
|---|---|---|---|---|
| Admin | Có / có | Có | Có | Xem và ghi |
| Project Director | Có / không | Có | Có | Xem và ghi |
| Project Manager | Có / không | Có | Có | Xem và ghi |
| Site Manager | Có / không | Không | Có | Xem và ghi |
| Supervising Engineer | Có / không | Không | Có, khi được giao | Xem và ghi |
| Materials Staff | Có / không | Không | Không | Không |
| Worker | Có / không | Không | Có, khi được giao | Không |
| Client Representative | Có / không, với tổ chức khớp | Không | Không | Chỉ xem nghiệm thu |

Các dòng trong bảng đều **pass trong các fixture đã nêu**; các dự án archived, completed, cancelled được kiểm tra ẩn sửa Task và form Progress; POST Progress trên một dự án chỉ đọc bị chặn cho từng role. Các ca Admin+PM/PD, tài khoản inactive, sai tổ chức và membership inactive cũng được kiểm tra trong phạm vi test. Không suy diễn rằng mọi tổ hợp đa-role hoặc mọi đường ghi trong hệ thống đã được kiểm thử trên UI.

Một lần chạy UI trung gian có ba lỗi do selector test khớp cả công việc trong và ngoài scope với Admin; selector đã sửa và bộ UI cuối đã pass. Bằng chứng lần lỗi nằm tại [TestResults](../../tests/WbsUi.Tests/TestResults) (`task05-Admin*`, `task05-AdminPD*`, `task05-AdminPM*`); chúng là log/screenshot của lần chạy trung gian, không phải lỗi còn tồn tại của lần chạy cuối.

### File thay đổi Đợt 2

| Nhóm | File |
|---|---|
| Controller/service | [CompletionController.cs](../../Controllers/CompletionController.cs), [CompletionService.cs](../../Services/CompletionService.cs), [ICompletionService.cs](../../Services/Interfaces/ICompletionService.cs), [TasksController.cs](../../Controllers/TasksController.cs) |
| Model/ViewModel | [TaskViewModels.cs](../../ViewModels/TaskViewModels.cs), [TaskStatus.cs](../../Models/TaskStatus.cs), [TaskPriority.cs](../../Models/TaskPriority.cs), [IncidentPriority.cs](../../Models/IncidentPriority.cs), [IncidentStatus.cs](../../Models/IncidentStatus.cs), [RfiStatus.cs](../../Models/RfiStatus.cs), [InspectionResult.cs](../../Models/InspectionResult.cs), [EnumDisplayExtensions.cs](../../Models/EnumDisplayExtensions.cs) mới |
| View | [Tasks/Index.cshtml](../../Views/Tasks/Index.cshtml), [Tasks/Details.cshtml](../../Views/Tasks/Details.cshtml), [Tasks/Create.cshtml](../../Views/Tasks/Create.cshtml), [Tasks/_TaskForm.cshtml](../../Views/Tasks/_TaskForm.cshtml), [Progress/Index.cshtml](../../Views/Progress/Index.cshtml), [Quality/Index.cshtml](../../Views/Quality/Index.cshtml), [Completion/Index.cshtml](../../Views/Completion/Index.cshtml) |
| Test | [TaskIntegrationTests.cs](../../tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs), [Task05UiTests.cs](../../tests/WbsUi.Tests/Task05UiTests.cs), [WbsUiFixture.cs](../../tests/WbsUi.Tests/WbsUiFixture.cs) |
| Tài liệu | Báo cáo này |

### Lưu ý còn lại

- Cảnh báo dependency đã tồn tại: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 và `System.Security.Cryptography.Pkcs` 9.0.0 có advisory; chưa nâng phiên bản ngoài phạm vi yêu cầu. Build cũng có cảnh báo `Contract.ProjectId` obsolete.
- Dữ liệu chi phí/bàn giao và thao tác Completion ngoài danh sách Task vẫn theo quyền cũ như người dùng đã chọn; không kết luận các dữ liệu này được lọc theo scope.
- Kết quả test chứng minh những kịch bản được chạy, không chứng minh mọi tổ hợp dữ liệu lịch sử, đa-role và liên module. Những chức năng ngoài phạm vi Đợt 2 nêu trong ảnh chụp lịch sử cần được xem xét theo phạm vi nghiệp vụ riêng.

## Cập nhật Đợt 1

### Đã triển khai

- [TaskAccess](../../Services/TaskAccess.cs) kiểm tra tài khoản active và phạm vi dự án cho Task/Progress/Quality/Dashboard; role hiện tại được controller lấy từ Identity. PD không còn được coi là Admin. Dự án archived/completed/cancelled vẫn được xem theo scope nhưng không được ghi. Có Client Representative thì luôn cần membership active và tổ chức khớp; quyền đa-role được cộng theo hợp quyền.
- [TaskService](../../Services/TaskService.cs), [TasksController](../../Controllers/TasksController.cs) và các view Task kiểm tra quyền dự án gốc trước GET/POST Edit kể cả khi lỗi validation, lọc options theo scope và dự án được chọn, ẩn thao tác sửa khi không có quyền ghi.
- [ProgressService](../../Services/ProgressService.cs) và [ProgressController](../../Controllers/ProgressController.cs) tách quyền xem/ghi; Worker và SE cần vừa trong scope vừa được giao trực tiếp, trừ khi có role khác cấp quyền ghi theo quy tắc hợp quyền. UI chỉ hiện form khi được phép.
- [TaskLifecycle](../../Services/TaskLifecycle.cs) kiểm tra trạng thái/%, nghiệm thu mới nhất, đồng bộ ngày thực tế và ghi lịch sử khi trạng thái hoặc % thay đổi qua Edit, Progress và Quality.
- [QualityService](../../Services/QualityService.cs) và [QualityController](../../Controllers/QualityController.cs) kiểm tra quyền, scope và read-only khi tạo/ghi nghiệm thu. Client Representative đơn role chỉ xem nghiệm thu; khi có role Quality khác thì được cộng quyền role đó nhưng vẫn phải khớp tổ chức/membership. Không tạo yêu cầu mới khi Task đang Completed/100%; Failed ở 100% yêu cầu % mới dưới 100.
- Theo quyết định bổ sung, yêu cầu Pending **cũ hơn yêu cầu mới nhất** vẫn có thể ghi kết quả và lịch sử nghiệm thu để xử lý dữ liệu cũ, nhưng không thay đổi trạng thái/% Task. Chỉ kết quả của yêu cầu mới nhất quyết định hiệu lực nghiệm thu.

### Kết quả kiểm thử Đợt 1

| Bộ kiểm thử | Kết quả | Cách ly |
|---|---:|---|
| [ConstructionProjectManagement.Tests](../../tests/ConstructionProjectManagement.Tests) | **117 pass, 0 fail, 0 skipped** | SQLite in-memory |
| [WbsUi.Tests](../../tests/WbsUi.Tests) | **16 pass, 0 fail, 0 skipped** | SQLite tạm và Playwright/Edge; gồm 2 test UI TASK-05 |

Test mới tại [TaskIntegrationTests.cs](../../tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs) bao gồm ma trận tám role ở tầng service, scope/assignment, tài khoản inactive, đa-role, read-only, nhánh form lỗi, vòng đời %, ngày, lịch sử và nghiệm thu. [Task05UiTests.cs](../../tests/WbsUi.Tests/Task05UiTests.cs) kiểm tra form Task, nút theo trạng thái và giao diện chỉ xem của Client Representative. Hai test browser này không phải ma trận UI đầy đủ tám role.

Không thay đổi schema/migration và không dùng database chính trong các test. Build vẫn báo cảnh báo dependency đã tồn tại cho `SQLitePCLRaw.lib.e_sqlite3` và `System.Security.Cryptography.Pkcs`. Các mục ngoài phạm vi Đợt 1 như tìm/lọc Task, nhãn enum tiếng Việt và scope `CompletionController` vẫn cần xử lý riêng.

### File được cập nhật trong Đợt 1

| Nhóm | File |
|---|---|
| Controller | [TasksController.cs](../../Controllers/TasksController.cs), [ProgressController.cs](../../Controllers/ProgressController.cs), [QualityController.cs](../../Controllers/QualityController.cs), [DashboardController.cs](../../Controllers/DashboardController.cs) |
| Service | [TaskService.cs](../../Services/TaskService.cs), [ProgressService.cs](../../Services/ProgressService.cs), [QualityService.cs](../../Services/QualityService.cs), [DashboardService.cs](../../Services/DashboardService.cs), [TaskAccess.cs](../../Services/TaskAccess.cs) **mới**, [TaskLifecycle.cs](../../Services/TaskLifecycle.cs) **mới** |
| Interface | [ITaskService.cs](../../Services/Interfaces/ITaskService.cs), [IProgressService.cs](../../Services/Interfaces/IProgressService.cs), [IQualityService.cs](../../Services/Interfaces/IQualityService.cs) |
| ViewModel | [TaskViewModels.cs](../../ViewModels/TaskViewModels.cs), [ProgressViewModels.cs](../../ViewModels/ProgressViewModels.cs), [QualityViewModels.cs](../../ViewModels/QualityViewModels.cs) |
| View | [Tasks/Index.cshtml](../../Views/Tasks/Index.cshtml), [Tasks/Details.cshtml](../../Views/Tasks/Details.cshtml), [Tasks/Create.cshtml](../../Views/Tasks/Create.cshtml), [Tasks/Edit.cshtml](../../Views/Tasks/Edit.cshtml), [Tasks/_TaskForm.cshtml](../../Views/Tasks/_TaskForm.cshtml), [Progress/Index.cshtml](../../Views/Progress/Index.cshtml), [Quality/Index.cshtml](../../Views/Quality/Index.cshtml) |
| Test | [TaskIntegrationTests.cs](../../tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs) cập nhật, [Task05UiTests.cs](../../tests/WbsUi.Tests/Task05UiTests.cs) **mới** |
| Tài liệu | Báo cáo này |

---

**Ngày lập:** 23/11/2026  
**Kết luận:** **Chưa hoàn thành; chưa đủ cơ sở triển khai TASK-05 như một chức năng đã được bảo đảm phân quyền và nghiệp vụ.**

Báo cáo đối chiếu mã nguồn hiện tại, ba tài liệu yêu cầu và những quyết định nghiệp vụ đã được người dùng xác nhận trong cuộc trao đổi. Kết quả kiểm thử được trích từ lần chạy trước trong cùng phiên; lượt lập báo cáo này không chạy lại test, không sửa source, migration hoặc dữ liệu.

## 1. Căn cứ và cách đánh giá

### Tài liệu yêu cầu

1. [Ma trận phân quyền tám chức vụ](../../../ma_tr_n_ph_n_quy_n_8_ch_c_v_task_05.md).
2. [Đặc tả dữ liệu và kiến trúc tích hợp](../../../t_i_li_u_d_li_u_ki_n_tr_c_h_th_ng_task_05.md).
3. [Đặc tả nghiệp vụ quản lý công việc](../../../t_i_li_u_nghi_p_v_task_05_qu_n_l_c_ng_vi_c.md).

Các quyết định bổ sung đã được chốt trong trao đổi: khóa đổi dự án sau khi tạo Task; quyền đa-role là hợp các quyền, nhưng nếu có Client Representative thì luôn phải có membership active và khớp tổ chức; Worker và SE cần vừa thuộc phạm vi vừa được giao trực tiếp để ghi tiến độ; Admin được ghi nghiệm thu toàn hệ thống nếu dự án cho phép ghi, vẫn tuân thủ điều kiện Client Representative nếu có role này; Client Representative chỉ xem phần nghiệm thu trong Quality. Chỉ được bỏ `RequiresInspection` trước khi tạo yêu cầu nghiệm thu đầu tiên.

Quy tắc trạng thái và nghiệm thu đã chốt được liệt kê tại mục 5.2–5.3. Giữ `decimal` cho phần trăm và ngày kế hoạch `DateTime` bắt buộc theo quyết định của người dùng, thay cho kiểu `int` và ngày nullable trong đặc tả dữ liệu; không xem khác biệt kiểu này là lỗi cần migration.

**Nhãn trong báo cáo:**

- **Đạt trong phạm vi kiểm tra:** có code tương ứng và có test pass cho phạm vi nêu rõ; không đồng nghĩa toàn bộ chức năng đã đạt.
- **Có một phần:** có implementation hoặc kiểm tra, nhưng chưa đáp ứng toàn bộ yêu cầu hay chưa đủ test.
- **Chưa đạt:** mã hiện tại thiếu hoặc mâu thuẫn với yêu cầu đã chốt.
- **Chưa xác minh:** chưa có bằng chứng kiểm thử thích hợp; không đồng nghĩa thất bại.

[Báo cáo discovery trước đó](TASK05_SOURCE_CODE_DISCOVERY_AUDIT.md) là ảnh chụp lịch sử trước các thay đổi sau audit; những nhận định về chuyển dự án, chu kỳ cha–con, timestamp phân công và liên kết vật tư/thiết bị trong báo cáo cũ không còn phản ánh đầy đủ mã hiện tại.

## 2. Tổng quan theo thành phần

| Thành phần | Hiện trạng | Đánh giá |
|---|---|---|
| Cấu trúc dữ liệu Task | Có Task, Assignment, Dependency, ProgressHistory; liên kết Project, WBS, Identity và nghiệm thu | **Có một phần**: đủ nền tảng, chưa chứng minh toàn vẹn nghiệp vụ ở mọi luồng |
| Tạo/sửa Task | Có form, controller, service và một số chốt chặn mới | **Có một phần**: các đường đọc, form và validation còn lỗ hổng |
| Phân công | Kiểm tra tài khoản active và thuộc dự án; cập nhật theo diff | **Đạt trong phạm vi test** giữ `AssignedAt`; chưa chứng minh đầy đủ các tổ hợp quyền |
| Cây Task/WBS | Kiểm tra cùng dự án, chu kỳ cha–con, quyền WBS khi đổi liên kết | **Có một phần**: đã có test trọng tâm; chưa kiểm thử UI TASK-05 |
| Progress | Có cập nhật, lịch sử và biểu đồ | **Chưa đạt**: chưa tách quyền xem/ghi và chưa đảm bảo trạng thái, scope, read-only |
| Quality/nghiệm thu | Có yêu cầu và ghi kết quả | **Chưa đạt**: thiếu kiểm soát scope/read-only và quy tắc nghiệm thu mới nhất |
| Tích hợp Materials/Equipment | Kiểm tra Task cùng Project khi lập phiếu/bàn giao | **Đạt trong phạm vi test** liên kết TaskId; không khẳng định toàn bộ quyền của hai module |
| Completion/Dashboard | Có thống kê và danh sách Task | **Chưa đạt** theo yêu cầu scope của TASK-05 |
| Giao diện Task/Progress | Có các trang cơ bản | **Chưa đạt**: lọc, nhãn và ẩn thao tác theo quyền/trạng thái chưa đầy đủ |
| Kiểm thử | Có test service/integration mới và test WBS hiện hữu | **Có một phần**: chưa có bộ UI TASK-05 và ma trận tám role đầy đủ |

## 3. Thành phần đã đạt trong phạm vi kiểm tra

### 3.1. Ràng buộc Task và phân công

Tại [TaskService.cs](../../Services/TaskService.cs):

1. `UpdateAsync` tải Task theo ID và `ValidateAsync` từ chối đổi `ProjectId`; form sửa tại [_TaskForm.cshtml](../../Views/Tasks/_TaskForm.cshtml) cũng khóa lựa chọn dự án. Test service đã xác nhận trường hợp chuyển dự án bị từ chối. **Chưa có test UI xác nhận form thực tế.**
2. `ValidateAsync` duyệt tổ tiên của `ParentTaskId`, kiểm tra cùng dự án và từ chối vòng lặp. Test đã kiểm tra một vòng lặp cha–con; không nên hiểu là mọi độ sâu và mọi dữ liệu lịch sử đã được test.
3. Người được giao phải active và là PM chính hoặc thành viên active. Khi sửa, service thêm/bớt theo danh sách thay đổi; test xác nhận `AssignedAt` của người vẫn được giao không đổi.
4. Đường ghi Task trong service kiểm tra role Admin/PD/PM, tài khoản active, dự án không archived/completed/cancelled, và scope dự án. Nếu có role Client Representative, service yêu cầu membership active và tổ chức khớp, kể cả khi có Admin. Các test đã kiểm tra bốn trường hợp dự án/tài khoản và một trường hợp Admin + Client Representative sai tổ chức; chưa bao phủ đủ ma trận.
5. Đã chặn bỏ `RequiresInspection` sau khi có yêu cầu nghiệm thu; hai test kiểm tra trường hợp trước và sau yêu cầu đầu tiên.
6. Kiểm tra ngày kết thúc kế hoạch không trước ngày bắt đầu và phần trăm trong khoảng 0–100. Kiểm tra này **chưa** đảm bảo trạng thái và phần trăm nhất quán.

Những kiểm tra ghi ở trên không thay thế kiểm tra quyền đọc và trả form tại controller; các điểm còn thiếu được nêu ở mục 5.

### 3.2. Liên kết WBS

`TaskService.ValidateAsync` kiểm tra WBS cùng dự án và gọi `IWbsService.CanLinkWorkItemToTaskAsync` khi gắn mới, đổi hoặc bỏ liên kết. Khi liên kết không đổi, WBS đã archive vẫn được bảo toàn. Test WBS hiện hữu kiểm tra trường hợp giữ liên kết archived khi sửa Task tại [WbsServiceTests.cs](../../tests/ConstructionProjectManagement.Tests/WbsServiceTests.cs). Quyền gắn WBS vẫn độc lập với quyền tạo/sửa Task; không mặc định mọi người có quyền sửa Task đều được gắn WBS.

### 3.3. Liên kết vật tư và thiết bị

- [MaterialService.cs](../../Services/MaterialService.cs), `CreateRequestAsync`: nếu có `TaskId`, chỉ chấp nhận Task tồn tại và thuộc `ProjectId` của phiếu.
- [EquipmentService.cs](../../Services/EquipmentService.cs), `AssignAsync`: cùng quy tắc cho lệnh bàn giao; kiểm tra trước khi chuyển thiết bị sang `InUse`.
- Trong [TaskIntegrationTests.cs](../../tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs), mỗi service có bốn ca: không chọn Task, Task cùng dự án, Task khác dự án, Task không tồn tại. Tất cả tám ca đã pass; ca từ chối còn kiểm tra không tạo bản ghi, riêng thiết bị kiểm tra trạng thái không đổi.

Phạm vi test này chỉ chứng minh ràng buộc Task–Project tại hai phương thức trên, không chứng minh quyền người dùng hay trạng thái dự án cho toàn bộ module vật tư/thiết bị.

## 4. Database và migration

Các entity lõi hiện có: [Task.cs](../../Models/Task.cs), [TaskAssignment.cs](../../Models/TaskAssignment.cs), [TaskDependency.cs](../../Models/TaskDependency.cs), [TaskProgressHistory.cs](../../Models/TaskProgressHistory.cs). [ApplicationDbContext.cs](../../Data/ApplicationDbContext.cs) khai báo DbSet và cấu hình quan hệ. `Task` có `ProjectId`, `WorkItemId?`, `ParentTaskId?`, trạng thái, phần trăm, ngày, cờ nghiệm thu và assignments. Cơ sở dữ liệu còn có [InspectionRequest.cs](../../Models/InspectionRequest.cs) và [InspectionHistory.cs](../../Models/InspectionHistory.cs).

Schema Task SQLite có trong [InitialSqlite](../../Migrations/20261111092125_InitialSqlite.cs); migration WBS tại [Task04WbsLifecycle](../../Migrations/20261122070416_Task04WbsLifecycle.cs). **Không tạo migration TASK-05 mới trong phần thay đổi đã báo cáo.** Test service sử dụng SQLite trong bộ nhớ; test UI WBS dùng SQLite tạm. Không có bằng chứng từ các test này để kết luận migration nào đang áp dụng hoặc dữ liệu nào đang tồn tại trong DB chính.

`TaskDependency` hiện có model/schema, nhưng không đồng nghĩa đã có luồng UI hoặc service quản lý phụ thuộc. Cần xác nhận phạm vi nghiệp vụ trước khi mở rộng chức năng này.

## 5. Thành phần chưa đạt và ảnh hưởng

### 5.1. Phân quyền Task và dữ liệu form — mức ưu tiên cao

| Vấn đề | Bằng chứng | Ảnh hưởng |
|---|---|---|
| Quyền đọc Task chưa theo ma trận | `TasksController.Index/Details` truyền `isAdmin = Admin hoặc PD`; `TaskService.VisibleQuery` cho `isAdmin` xem mọi dự án chưa archive, hoặc người khác xem khi là PM, member active **hoặc được giao** | PD có thể thấy Task ngoài scope; assignment đơn lẻ có thể cho phép đọc dù không còn membership. Chưa kiểm tra ClientName và tài khoản active tại query này |
| Form Edit và nhánh dữ liệu lỗi chưa kiểm tra đủ đối tượng gốc | `TasksController.Edit` GET dùng `GetByIdAsync`; POST khi `ModelState` lỗi vẫn nạp options và trả form ngay cả khi Task không tìm thấy/không đủ quyền ghi | Có nguy cơ lộ dữ liệu form hoặc mở giao diện sửa không đúng quyền; service chặn khi lưu thành công không đủ để bảo vệ các nhánh đọc |
| Form nạp dữ liệu quá rộng | `LoadOptionsAsync` nạp mọi project chưa archive, mọi Task và user active; chưa lọc danh sách theo scope/dự án được chọn, cũng chưa loại dự án Completed/Cancelled | Lộ lựa chọn ngoài phạm vi; người dùng có thể chọn phương án mà service sau đó từ chối |
| UI chưa phản ánh read-only | [Details.cshtml](../../Views/Tasks/Details.cshtml) hiện nút cập nhật theo role, không kiểm tra trạng thái dự án; form Edit có thể mở dù service từ chối ghi | Trải nghiệm và điều kiện GET/UI không đồng bộ với POST/service |

Quyền xem các Task trong dự án archived cũng cần được xử lý theo chính sách xem/read-only đã chốt: `VisibleQuery` hiện loại toàn bộ dự án `IsArchived`, thay vì phân biệt xem và ghi.

### 5.2. Trạng thái, phần trăm và lịch sử — mức ưu tiên cao

Các quyết định đã chốt: `Pending = 0%`; `InProgress` từ 0 đến dưới 100%; `Completed = 100%`; `Assigned`, `PendingInspection`, `NeedsRework`, `Cancelled` có thể 0–100%, trừ Task cần nghiệm thu chưa có kết quả hợp lệ thì không được 100%. Khi dữ liệu không khớp phải từ chối, không tự sửa. `ActualStartDate` được ghi khi % lớn hơn 0 nếu chưa có; `ActualEndDate` được ghi khi đạt 100% và xóa khi giảm xuống dưới 100%.

| Hiện trạng | Bằng chứng và ảnh hưởng |
|---|---|
| Chưa đồng bộ trạng thái và phần trăm | `TaskService.ValidateAsync` và `ProgressService.UpdateAsync` chỉ kiểm tra khoảng 0–100; ví dụ `Pending` với 50% hoặc `Completed` với 20% chưa bị quy tắc chung ngăn chặn |
| Thay đổi qua Edit không ghi lịch sử | `TaskService.UpdateAsync` gán trực tiếp `Status` và `PercentComplete`, không thêm `TaskProgressHistory`; form còn cho nhập ngày thực tế |
| Progress chỉ ghi lịch sử trên luồng của nó | `ProgressService.UpdateAsync` thêm history, đặt `ActualStartDate` và `ActualEndDate`, nhưng không xóa `ActualEndDate` khi % giảm dưới 100 |
| Quality cũng thay đổi Task nhưng không ghi lịch sử tiến độ | `QualityService.CreateInspectionAsync/RecordInspectionAsync` thay đổi trạng thái hoặc %, không ghi `TaskProgressHistory`; chưa bảo đảm quy tắc ngày thực tế chung |

### 5.3. Nghiệm thu — mức ưu tiên cao

Theo quyết định đã chốt, nghiệm thu hợp lệ là **yêu cầu nghiệm thu mới nhất có kết quả Passed**. Yêu cầu mới Pending/Failed làm kết quả Passed cũ không còn đủ điều kiện. Task yêu cầu nghiệm thu chỉ được 100% hoặc Completed khi có kết quả hợp lệ; sau đó có thể hoàn thành qua Edit/Progress. Nếu ghi Failed khi Task đang 100%, người ghi phải nhập % mới dưới 100 và ghi lịch sử. Không tạo yêu cầu nghiệm thu mới khi Task cần nghiệm thu đang Completed/100%; phải hạ % trước.

Mã hiện tại trong [QualityService.cs](../../Services/QualityService.cs), [QualityController.cs](../../Controllers/QualityController.cs) và [Quality/Index.cshtml](../../Views/Quality/Index.cshtml) **chưa đáp ứng**:

- Tạo/ghi nghiệm thu chưa kiểm tra đầy đủ `User.IsActive`, scope theo Task gốc, đa-role, tổ chức khách hàng và dự án chỉ đọc. Index và danh sách chọn Task cũng chưa lọc theo scope.
- `TaskService` và `ProgressService` từ chối `Completed` với mọi Task yêu cầu nghiệm thu, kể cả đã Passed hợp lệ; đồng thời chưa có quy tắc kiểm tra lần nghiệm thu mới nhất khi % đạt 100.
- `CreateInspectionAsync` chưa chặn tạo yêu cầu mới khi Task đang 100%.
- `RecordInspectionAsync` đặt `NeedsRework` khi Failed nhưng giữ phần trăm cũ; có thể còn 100%, không có trường nhập % mới và không xóa ngày kết thúc thực tế trong nhánh này.
- `RecordInspectionAsync` không kiểm tra yêu cầu đang Pending trước khi ghi; UI chỉ hiện form cho Pending, nhưng server không có chốt tương ứng.
- Client Representative đơn role hiện bị attribute của `QualityController` loại khỏi toàn bộ trang. Yêu cầu đã xác nhận là cho xem **chỉ nghiệm thu**, không cho xem sự cố/RFI và không cho ghi nếu chỉ có role này; phần này chưa triển khai.

### 5.4. Quyền Progress — mức ưu tiên cao

[ProgressController.cs](../../Controllers/ProgressController.cs) chỉ yêu cầu đăng nhập cho `Update`; [ProgressService.cs](../../Services/ProgressService.cs) dùng cùng `VisibleQuery` cho đọc và ghi. Query cho phép PM chính, member active **hoặc người được assignment**, còn controller coi PD như Admin. Chưa thấy chốt chặn riêng theo role, `User.IsActive`, ClientName, assignment bắt buộc cho Worker/SE hay dự án Completed/Cancelled. `IsArchived` chỉ bị loại khỏi query.

Vì vậy, có nguy cơ người chỉ được xem hoặc người không còn thuộc scope vẫn ghi tiến độ; form trong [Progress/Index.cshtml](../../Views/Progress/Index.cshtml) cũng hiển thị thao tác lưu cho mọi Task được liệt kê. Không thể dùng việc ẩn nút UI thay cho kiểm tra server.

### 5.5. Tích hợp và giao diện — mức ưu tiên cao/trung bình

- [CompletionController.cs](../../Controllers/CompletionController.cs), `Index`: truy vấn `OpenTasks` toàn hệ thống rồi lấy tối đa 100, chưa lọc theo scope dự án.
- [DashboardController.cs](../../Controllers/DashboardController.cs) truyền `isAdmin = Admin hoặc PD`; [DashboardService.cs](../../Services/DashboardService.cs) lọc theo biến này, vì vậy PD có thống kê Task toàn hệ thống; chưa áp dụng điều kiện Client Representative.
- [Tasks/Index.cshtml](../../Views/Tasks/Index.cshtml) chưa có tìm/lọc theo dự án, trạng thái, ưu tiên, từ khóa; hiển thị enum tiếng Anh thô. Các view Task/Progress khác cũng hiển thị enum trực tiếp.
- [TaskViewModels.cs](../../ViewModels/TaskViewModels.cs) chưa đồng bộ `MaxLength` cho tiêu đề/mô tả với [Task.cs](../../Models/Task.cs); service có kiểm tra thứ tự ngày nhưng chưa chuẩn hóa đầy đủ dữ liệu và ngày thực tế giữa các luồng.
- Không có test UI TASK-05 chứng minh bộ lọc, form và ẩn/hiện nút theo tám role/read-only. Script nạp lại lựa chọn dự án trong partial chưa được xác minh bằng browser test TASK-05.

## 6. Ma trận quyền: yêu cầu và thực tế

Ma trận dưới đây mô tả **yêu cầu đã chốt**, không phải kết quả pass của tám tài khoản. `Scope` là PM chính hoặc membership active; với Client Representative luôn cần membership active và tổ chức khớp. Mọi ghi cần dự án cho phép ghi.

| Role đơn | Xem Task/Progress | Tạo/sửa Task | Ghi Progress | Ghi nghiệm thu |
|---|---|---|---|---|
| Admin | Toàn hệ thống | Toàn hệ thống | Toàn hệ thống | Toàn hệ thống |
| Project Director | Scope | Scope | Scope | Scope |
| Project Manager | Scope | Scope | Scope | Scope |
| Site Manager | Scope | Không | Scope | Scope |
| Supervising Engineer | Scope | Không | Scope **và được giao trực tiếp** | Scope |
| Materials Staff | Scope | Không | Không | Không |
| Worker | Scope | Không | Scope **và được giao trực tiếp** | Không |
| Client Representative | Membership active + khớp tổ chức | Không | Không | Không; được xem nghiệm thu trong scope |

**Đối chiếu thực tế:** đường ghi Task tại service đã có một số kiểm tra tương ứng, nhưng chưa có test toàn ma trận. Đường đọc Task/Progress, ghi Progress và Quality hiện có các sai lệch nêu tại mục 5. Với đa-role, không được suy luận quyền chỉ từ một dòng; phải lấy hợp quyền và áp dụng điều kiện Client Representative bắt buộc khi role này hiện diện. Chưa có bộ test toàn diện chứng minh quy tắc đó trên các endpoint và service.

## 7. Kiểm thử đã chạy và giới hạn

Kết quả dưới đây là từ lần chạy trước trong cùng phiên, không phải test chạy lại khi lập báo cáo:

| Bộ kiểm thử | Kết quả | Phạm vi và giới hạn |
|---|---:|---|
| `ConstructionProjectManagement.Tests` toàn project | **64 pass, 0 fail, 0 skipped** | Test service/integration hiện có, gồm test TASK-05 mới. Chạy trước lần di chuyển thứ tự kiểm tra cuối cùng trong `TaskService` |
| Test trọng tâm `TaskIntegrationTests` + test giữ WBS archived | **18 pass, 0 fail, 0 skipped** | Chạy lại sau thay đổi cuối: 17 ca integration mới và 1 ca WBS hiện hữu |
| `WbsUi.Tests` | **14 pass, 0 fail, 0 skipped** | UI WBS hiện có, không phải bộ UI TASK-05; chạy trước lần di chuyển thứ tự kiểm tra cuối trong `TaskService` |

Lệnh đã dùng có `--no-restore`; test service dùng SQLite in-memory và UI WBS dùng SQLite tạm. Không khởi động ứng dụng với connection string DB chính. Chưa tạo/chạy UI test TASK-05, chưa xác minh đầy đủ tám role, đa-role, phạm vi dự án, Quality, Progress và các nhánh lỗi end-to-end.

Build/test phát cảnh báo phụ thuộc hiện có về `SQLitePCLRaw.lib.e_sqlite3` và `System.Security.Cryptography.Pkcs`; chưa có thay đổi package trong phần công việc này. Cảnh báo không đồng nghĩa test đã thất bại, nhưng cần được đánh giá riêng.

## 8. Các file đã thay đổi trong phần implementation trước báo cáo

| File | Nội dung |
|---|---|
| [TaskService.cs](../../Services/TaskService.cs) | Khóa chuyển dự án, kiểm tra một số quyền ghi/read-only, chống chu kỳ, bảo toàn assignment, giới hạn bỏ yêu cầu nghiệm thu |
| [TasksController.cs](../../Controllers/TasksController.cs) | Bỏ đổi dự án qua tham số GET Edit |
| [_TaskForm.cshtml](../../Views/Tasks/_TaskForm.cshtml) | Khóa chọn dự án khi sửa |
| [MaterialService.cs](../../Services/MaterialService.cs) | Kiểm tra Task thuộc dự án của phiếu vật tư |
| [EquipmentService.cs](../../Services/EquipmentService.cs) | Kiểm tra Task thuộc dự án của bàn giao thiết bị |
| [TaskIntegrationTests.cs](../../tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs) | Test SQLite in-memory cho các ràng buộc trên |

Danh sách này không hàm ý toàn bộ yêu cầu TASK-05 đã được triển khai. Báo cáo hiện tại là tài liệu bổ sung; không sửa các file code trên trong lượt lập báo cáo.

## 9. Thứ tự hoàn thiện đề xuất

1. **Bảo vệ server trước:** thống nhất kiểm tra user active, role union, scope, Client Representative, dự án read-only và đối tượng DB gốc cho mọi luồng Task/Progress/Quality, kể cả nhánh ModelState lỗi; lọc dữ liệu form.
2. **Chuẩn hóa vòng đời:** gom và kiểm thử quy tắc trạng thái/%, latest inspection, ngày thực tế và `TaskProgressHistory` trên cả Task, Progress, Quality; triển khai nhánh Failed từ 100% và quy tắc tạo yêu cầu mới.
3. **Hoàn thiện tích hợp và UI:** lọc Completion/Dashboard; cho Client Representative chỉ xem nghiệm thu; bổ sung lọc, nhãn tiếng Việt, hiển thị thao tác đúng quyền/read-only.
4. **Kiểm thử toàn diện trên DB tách biệt:** unit/integration và Playwright cho tám role, đa-role, inactive, mismatch tổ chức, scope/assignment, dự án read-only, request lỗi, cây/WBS, nghiệm thu và tích hợp. Chạy lại toàn bộ test và báo cáo từng ca.

**Điều kiện kết thúc:** chỉ đánh dấu TASK-05 hoàn thành sau khi các khoảng trống trên được triển khai và có kết quả kiểm thử phù hợp; các kết quả WBS và test trọng tâm hiện tại không đủ chứng minh điều đó.
