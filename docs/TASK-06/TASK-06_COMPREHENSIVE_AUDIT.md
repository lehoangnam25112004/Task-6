# TASK-06 – Comprehensive Codebase Audit

## Phạm vi và phương thức

Báo cáo này rà soát source code liên quan đến **TASK-06 – Theo dõi công việc/Kế hoạch và tiến độ** trong solution `ConstructionProjectManagement`.

Phạm vi truy vết bao gồm Task Tracking dùng chung với TASK-05, Project/WBS, Identity/ProjectMember, Quality/Inspection, Completion/Dashboard, tài liệu và test. Lần rà soát này chỉ đọc source; không chạy migration/test ghi database, không sửa source nghiệp vụ, không gọi seed và không thay đổi database.

## PHẦN 1 – TỔNG QUAN

### Tình trạng chung

TASK-06 **đã có phần triển khai thực tế ở mức lõi**, chủ yếu là:

- xem danh sách công việc trong phạm vi project;
- xem biểu đồ kế hoạch/thực tế;
- cập nhật `%`, trạng thái và lý do chậm;
- ghi `TaskProgressHistory`;
- đồng bộ `ActualStartDate`/`ActualEndDate`;
- kiểm tra scope, User active, project read-only và quyền trực tiếp với công việc.

Không tìm thấy:

- workflow riêng `Draft → Submitted → Approved`;
- quy trình phê duyệt thay đổi quan trọng;
- comment/thảo luận/cập nhật trao đổi;
- tìm kiếm/lọc/pagination riêng trong trang Progress;
- API/report/export Task Tracking;
- quy tắc thực thi cho `TaskDependency`;
- cơ chế optimistic concurrency/row version;
- cancel workflow riêng hoặc audit chung cho mọi thay đổi.

Mức hoàn thiện ước lượng: **phần lõi khoảng 55–65%**, chưa đủ nếu tiêu chí TASK-06 bao gồm theo dõi toàn vòng đời, phê duyệt, phụ thuộc, bình luận và audit đầy đủ.

## PHẦN 2 – DANH SÁCH FILE

| STT | File Path | Thành phần | Chức năng | Mức liên quan | Tình trạng |
|---:|---|---|---|---|---|
| 1 | `Models/Task.cs` | Entity Task | Project, WBS, cha/con, ngày, priority, status, %, inspection | Direct/Shared | Có triển khai |
| 2 | `Models/TaskAssignment.cs` | Phân công | liên kết Task–User, unique theo Task/User | Direct/Shared | Có triển khai |
| 3 | `Models/TaskDependency.cs` | Phụ thuộc Task | Task và predecessor Task | Direct | Entity-only, chưa có flow |
| 4 | `Models/TaskProgressHistory.cs` | Lịch sử tiến độ | trước/sau status và %, lý do chậm, người, thời gian | Direct | Có triển khai |
| 5 | `Models/TaskStatus.cs` | Enum trạng thái | 7 trạng thái hiện tại | Direct | Có triển khai |
| 6 | `Models/TaskPriority.cs` | Enum ưu tiên | 4 mức | Direct | Có triển khai |
| 7 | `Models/WorkItem.cs` | WBS | liên kết Task–WBS | Indirect/Shared | Có triển khai |
| 8 | `Models/Project.cs` | Project | project, PM, status, archive | Indirect/Shared | Có triển khai |
| 9 | `Data/ApplicationDbContext.cs:355-423` | Cấu hình EF | Quan hệ Task/WBS/assignment/dependency/history | Direct/Shared | Có triển khai |
| 10 | `Services/TaskAccess.cs:10-76` | Phân quyền phạm vi | 8 role, active, membership, PM, client, read-only | Direct/Shared | Có triển khai |
| 11 | `Services/TaskLifecycle.cs:8-58` | Workflow trạng thái/% | validation, history, actual dates | Direct | Triển khai một phần |
| 12 | `Services/ProgressService.cs:10-94` | Service tiến độ | visible, update, chart | Direct | Có triển khai |
| 13 | `Services/Interfaces/IProgressService.cs` | Interface tiến độ | contract progress/chart | Direct | Có triển khai |
| 14 | `Controllers/ProgressController.cs` | Controller tiến độ | Index, Update, ChartData | Direct | Có triển khai |
| 15 | `ViewModels/ProgressViewModels.cs` | ViewModel tiến độ | update/charts | Direct | Có triển khai |
| 16 | `Views/Progress/Index.cshtml` | Giao diện tiến độ | bảng cập nhật, cảnh báo chậm, biểu đồ | Direct | Có triển khai |
| 17 | `Services/TaskService.cs` | Task lifecycle chung | Edit/Create/Update/history validation | Direct/Shared | Có triển khai |
| 18 | `Controllers/TasksController.cs` | Quản lý Task | list/detail/create/edit/filter | Direct/Shared | Có triển khai |
| 19 | `Views/Tasks/*.cshtml` | Giao diện Task | list/detail/form | Direct/Shared | Có triển khai |
| 20 | `Services/QualityService.cs` | Nghiệm thu | Passed/Failed có tác động Task và lịch sử | Indirect/Shared | Có triển khai |
| 21 | `Services/CompletionService.cs` | Hoàn tất | task mở, handover, archive | Indirect/Shared | Có triển khai |
| 22 | `Services/DashboardService.cs` | Tổng quan | open/overdue task count | Indirect/Shared | Có triển khai |
| 23 | `Services/WbsService.cs:747-854` | Liên kết WBS | WBS cùng project, active/archive | Indirect/Shared | Có triển khai |
| 24 | `docs/01_Business_Analysis/Business_05_Task_Management.md` | Đặc tả Task | Business rule Project/WBS/assignment | Documentation/Shared | Có tài liệu |
| 25 | `docs/06_Diagrams/10_Process_04_Progress_Tracking.mmd` | Process diagram | chậm tiến độ | Documentation | Có tài liệu |
| 26 | `docs/06_Diagrams/16_Sequence_Progress_Update.mmd` | Sequence diagram | luồng cập nhật Progress | Documentation | Có tài liệu |
| 27 | `TASK_FUNCTION_INVENTORY.md:120-125` | Inventory | định nghĩa FN-06-01 đến FN-06-03 | Documentation | Có tài liệu |
| 28 | `tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs` | Integration test | scope, lifecycle, history, 8 role | Testing | Có test |
| 29 | `tests/WbsUi.Tests/Task05UiTests.cs` | UI test Task/Progress | quyền, read-only, scope, lọc | Testing | Có test |
| 30 | `tests/WbsUi.Tests/WbsUiFixture.cs` | UI fixture | SQLite tạm, server Playwright/Edge | Testing | Có fixture |

## PHẦN 3 – PHÂN TÍCH NGHIỆP VỤ

| Chức năng | Đánh giá | Bằng chứng |
|---|---|---|
| Xem công việc theo scope | Đã hoàn thành ở core | `TaskAccess`, `TaskService.VisibleQuery`, `ProgressService.VisibleQuery` |
| Cập nhật `%` | Đã hoàn thành | `ProgressController.Update`, `ProgressService.UpdateAsync` |
| Cập nhật trạng thái | Đã triển khai một phần | `TaskLifecycle.ValidateAsync` chỉ ép chặt Pending/InProgress/Completed; trạng thái khác chỉ giới hạn 0–100 |
| Ngày thực tế | Đã triển khai | `TaskLifecycle.Apply`: >0 tạo ActualStart; 100 tạo ActualEnd; <100 xóa ActualEnd |
| Lịch sử thay đổi trạng thái/% | Đã triển khai | `TaskLifecycle.Apply` ghi `TaskProgressHistory` nếu status hoặc % đổi |
| Lý do chậm | Đã triển khai | Progress yêu cầu lý do khi dự kiến đã quá hạn và `% < 100` |
| Cảnh báo quá hạn | Có triển khai ở UI và service | `ProgressService.isOverdue`, `Views/Progress/Index.cshtml` |
| Biểu đồ kế hoạch/thực tế | Đã triển khai, thiếu xử lý lỗi client | `ProgressController.ChartData`, `GetChartAsync`, inline fetch/Chart.js |
| Tìm kiếm/lọc/phân trang tiến độ | Chưa triển khai | Trang Progress không có filter hoặc pagination |
| Báo cáo/Export | Chưa tìm thấy | Không có endpoint/report/export riêng |
| Phụ thuộc công việc | Chưa triển khai | `TaskDependency` và bảng EF tồn tại; không có service/controller/view dùng dữ liệu này |
| Công việc cha/con | Có triển khai một phần | `ParentTaskId`/chuỗi vòng lặp có validation trong `TaskService`; không có giao diện cây theo TASK-06 |
| Liên kết WBS | Đã hoàn thành ở Task form | `TaskService.ValidateAsync`, `WbsService.GetTaskWorkItemsAsync/CanLink...` |
| Comment/thảo luận | Chưa triển khai | Không tìm thấy model/controller/view/comment flow |
| Hủy công việc | Chỉ qua trạng thái enum/edit/progress | Không có cancel workflow, lý do bắt buộc chung hoặc action chuyên biệt |
| Hoàn thành công việc | Đã hỗ trợ nhưng phụ thuộc nghiệm thu | `TaskLifecycle.ValidateAsync`, Quality service |
| Project read-only | Đã triển khai | `TaskAccess.IsWritable` chặn Archived/Completed/Cancelled |
| Draft/Submitted/Approved | Chưa triển khai | Không có enum trạng thái hoặc action approve/submit tương ứng |
| Lịch sử người thực hiện mọi thay đổi | Chưa đủ | Chỉ `TaskProgressHistory` cho thay đổi status/%; Task create/edit cũng không có audit class riêng |
| Xử lý xung đột đồng thời | Chưa triển khai | Không tìm thấy RowVersion/ConcurrencyStamp hay ETag/conflict action |

## PHẦN 4 – PHÂN TÍCH KỸ THUẬT

### Model và Database

- `Task` có FK Project bắt buộc, WorkItem tùy chọn, ParentTask tùy chọn, `%` precision `5,2`, PlannedStart/End và ActualStart/End.
- `TaskProgressHistory` lưu previous/new status và percentage, `UpdatedById`, `UpdatedAt`, `DelayReason`.
- `TaskAssignment` có unique index `(TaskId, UserId)` tại `ApplicationDbContext.cs:377-391`.
- `TaskDependency` có FK cascade/restrict nhưng không có validation hoặc UI/service.
- Không tìm thấy constraint DB `%` 0–100 hoặc constraint status/%; validation chủ yếu nằm trong application service.
- Không có `RowVersion` trên Task hoặc ProgressHistory; `SaveChangesAsync` không xử lý xung đột đồng thời.
- Task có `TaskId` bằng 0 khi `TaskLifecycle.Apply` tạo history trong `CreateAsync`; đây là điểm NEEDS_VERIFICATION đối với việc tạo history trên Create. Cần test runtime không ghi database test thật.

### Controller và Service

- `ProgressController.Update` kiểm tra scope trước khi gọi service, sau đó service lại tải access và task, giảm rủi ro truy cập chéo.
- `TaskController.Edit` nạp task gốc từ DB và kiểm tra read-only/scope ngay trong cả GET/POST.
- `TaskService.UpdateAsync` nạp existing theo ID rồi kiểm tra project/model và scope; project transfer bị chặn.
- `TaskService` kiểm tra WBS cùng project, vòng cha/con, assignee active/member.
- `ProgressService.VisibleQuery` không giới hạn project read-only nhưng `CanUpdateProgress` chặn ghi; đây là read-only hợp lý.
- `CompletionService.GetOpenTasksAsync` đã lọc scope trước `Take(100)`.
- Không thấy transaction transaction-level riêng trong `TaskService.Create/Update/ProgressUpdate`; một `SaveChangesAsync` thường bọc toàn entity graph và thường transaction trong EF.

### Validation

- `%` 0–100 có ViewModel Range và lifecycle.
- Pending chỉ được 0; InProgress không được 100; Completed bắt buộc 100.
- `Assigned`, `PendingInspection`, `NeedsRework`, `Cancelled` chỉ cần `%` nằm trong 0–100 theo test; không ép giá trị nghiệp vụ chi tiết.
- Ngày kết thúc phải không trước ngày bắt đầu.
- Công việc cha phải cùng project và không tạo cycle.
- Assignee cần active và là PM/member của project.
- Lý do chậm bắt buộc khi quá hạn và `<100`.

### Giao diện

- Task list có filter Project/Status/Priority/keyword.
- Progress view có cảnh báo chậm, form inline cập nhật và biểu đồ.
- Enum trạng thái/priority đã có DisplayName tiếng Việt.
- `ActualStartDate/ActualEndDate` là readonly trên Task form.
- Script đổi project Task có confirm và reload lựa chọn.
- Progress dùng CDN Chart.js; không có HTTP status/error/fallback xử lý.
- Không có pagination Task/Progress.
- Không có View cho comment, approval, dependency hoặc audit history.

### Audit log

- `TaskProgressHistory` đủ cho trạng thái/%.
- Không tìm thấy audit log cho create Task, edit metadata, thay đổi assignee/WBS/parent/dependency, cancel và inspection.
- `TaskAssignment.AssignedAt` giữ timestamp khi edit assignee nhưng không ghi người phân công.

## PHẦN 5 – PHÂN TÍCH WORKFLOW

| Trạng thái/nguyên tắc | Trạng thái code | Kết luận |
|---|---|---|
| Draft: người tạo sửa/hủy | Không có Draft | Chưa đáp ứng |
| Submitted: không edit trực tiếp, chỉ submit/return/cancel | Không có Submitted hoặc quy trình | Chưa đáp ứng |
| Approved: thay đổi qua request | Không có Approved hoặc approval request | Chưa đáp ứng |
| In Progress: assignee cập nhật; PM phê duyệt thay đổi quan trọng | Assigned/Worker/SE chỉ update khi scope + assignment; không có approval | Một phần |
| Completed: không sửa history; adjustment ghi bổ sung | Completed có thể Edit/Progress nếu inspection hợp lệ; không có cơ chế adjustment riêng | Một phần/chưa đáp ứng hoàn chỉnh |
| Cancelled: giữ history/lý do hủy | Có trạng thái Cancelled nhưng không có lý do bắt buộc/action riêng | Một phần |

Luồng thực tế là cập nhật trực tiếp, không có bước submit/approve/return. Điều này mâu thuẫn với mô tả workflow trong yêu cầu audit nếu quy tắc đó được xem là bắt buộc; cần sản phẩm xác nhận trước khi thay đổi.

## PHẦN 6 – MA TRẬN ROLE

| Chức vụ | Xem Task/Progress | Tạo/sửa Task | Cập nhật Progress | Nghiệm thu/Quality | Ghi chú |
|---|---|:|:|:|---|
| Admin | Có trong project writable/scope; Admin không bị chặn bởi membership | Có trong project writable | Có theo scope | Có | Không tự đồng nhất Project Director |
| Project Director | Có trong scope/PM/membership | Có trong project writable | Có | Có | Không có quyền toàn hệ thống trừ Admin |
| Project Manager | Có trong scope/PM/membership | Có trong project writable | Có | Có | Nguồn scope rõ |
| Site Manager | Có trong membership/scope | Không quản lý Task | Có | Có | Không phải role task-manager |
| Supervising Engineer | Có trong membership/scope | Không quản lý Task | Chỉ khi được assignment và project writable | Có | Không assignment => không ghi Progress |
| Worker | Có trong membership/scope | Không | Chỉ khi được assignment và project writable | Không | Xem được, ghi Progress khi được giao |
| Materials Staff | Có trong membership/scope | Không | Không | Không | Xem Task nhưng không quản lý/quality |
| Client Representative | Có nếu active membership và tổ chức khớp | Không | Không | Chỉ xem; không ghi | Không thấy Incident/RFI |

Các role đều bị chặn trên project Completed/Cancelled/Archived với thao tác ghi. `TaskAccess.LoadAsync` kiểm tra `User.IsActive`.

## PHẦN 7 – PHỤ THUỘC TASK KHÁC

- **TASK-01:** Identity user/role cung cấp actor, role và audit `UpdatedById`.
- **TASK-02:** Project/ProjectMember/ProjectManager là nguồn scope.
- **TASK-03:** không có phụ thuộc trực tiếp trong Task tracking; Project là shared aggregate.
- **TASK-04:** Task có `WorkItemId`, WBS validation cùng project và trạng thái archive.
- **TASK-05:** chia sẻ `Task`, assignment, lifecycle, Task list/detail/edit.
- **TASK-07:** assignee lấy từ Identity/ProjectMember, nhưng Task không liên kết Employee/Team; khi employee hoặc team thay đổi không có logic đồng bộ riêng.
- **TASK-08/09:** Material/Equipment có TaskId tùy chọn; không có tác động tiến độ tự động tìm thấy.
- **TASK-10:** ConstructionLog không có TaskId trong audit hiện tại.
- **TASK-11:** Quality/Inspection đọc và cập nhật Task; Passed/Failed cập nhật Completed/NeedsRework.
- **TASK-12:** Completion dùng Task mở để chặn handover/archive.
- **TASK-13:** Dashboard tính open/overdue task theo scope.

## PHẦN 8 – DANH SÁCH VẤN ĐỀ

### T06-001 – Không triển khai dependency execution

- **File:** `Models/TaskDependency.cs`; `Data/ApplicationDbContext.cs:393-403`
- **Hiện trạng:** Entity và migration tồn tại nhưng không có service/controller/view/use-case.
- **Nguyên nhân:** Thiết kế model chưa nối với workflow Task.
- **Mức độ:** High
- **Ảnh hưởng:** Không chặn Task con/tiền nhiệm; không phát hiện công việc bị block bởi predecessor.
- **TASK liên quan:** TASK-04, TASK-05, TASK-06

### T06-002 – Không có workflow approve/submit/draft

- **File:** `Models/TaskStatus.cs`; `Models/Task.cs`; `Controllers/TasksController.cs`; `Services/TaskLifecycle.cs`
- **Hiện trạng:** Chỉnh sửa trực tiếp và chỉ có enum trạng thái kỹ thuật.
- **Nguyên nhân:** Code hiện tại không có state machine riêng.
- **Mức độ:** High
- **Ảnh hưởng:** Không đáp ứng quy trình DRAFT/SUBMITTED/APPROVED.
- **TASK liên quan:** TASK-05, TASK-06

### T06-003 – Thiếu audit toàn diện

- **File:** `Models/TaskProgressHistory.cs`; `Services/TaskLifecycle.cs`
- **Hiện trạng:** Chỉ ghi thay đổi status/%; không ghi create/edit, assignee, WBS, parent, dependency.
- **Mức độ:** High
- **Ảnh hưởng:** Khó truy nguyên mọi thay đổi công việc.
- **TASK liên quan:** TASK-05, TASK-06

### T06-004 – Không có optimistic concurrency

- **File:** `Models/Task.cs`; `Models/TaskProgressHistory.cs`; `Data/ApplicationDbContext.cs`
- **Hiện trạng:** Không có RowVersion/Concurrency token.
- **Mức độ:** High
- **Ảnh hưởng:** Hai update đồng thời có thể ghi đè giá trị cuối hoặc làm mất logic state.
- **TASK liên quan:** TASK-05, TASK-06

### T06-005 – Create Task có khả năng tạo history gắn sai TaskId

- **File:** `Services/TaskService.cs:34-50`; `Services/TaskLifecycle.cs:38-54`
- **Hiện trạng:** `Apply` chạy trước `dbContext.Tasks.Add(task)`, history được tạo với `TaskId = task.Id` khi ID hiện là 0.
- **Mức độ:** Medium/High; NEEDS_VERIFICATION
- **Ảnh hưởng:** Có thể mất history khi tạo Task hoặc gắn sai khóa.
- **Đề xuất:** Chỉ xác nhận bằng test SQLite rồi kiểm tra fix; chưa triển khai.
- **TASK liên quan:** TASK-05, TASK-06

### T06-006 – Không có test xung đột đồng thời

- **File:** `tests/ConstructionProjectManagement.Tests/TaskIntegrationTests.cs`
- **Hiện trạng:** Test chủ yếu chức năng trạng thái/scope/history nhưng không có concurrency.
- **Mức độ:** High
- **Ảnh hưởng:** Risk cập nhật song song chưa được kiểm chứng.
- **TASK liên quan:** TASK-05, TASK-06

### T06-007 – Không có filter/pagination trong Progress

- **File:** `Controllers/ProgressController.cs`; `Views/Progress/Index.cshtml`
- **Hiện trạng:** Nạp và render toàn bộ visible Task, không phân trang.
- **Mức độ:** Medium
- **Ảnh hưởng:** Hiệu năng khi số công việc lớn; thiếu bộ lọc project/status/keyword.
- **TASK liên quan:** TASK-06

### T06-008 – Biểu đồ phụ thuộc CDN và xử lý lỗi yếu

- **File:** `Views/Progress/Index.cshtml:82-104`
- **Hiện trạng:** CDN Chart.js, fetch không kiểm tra `response.ok`, không có catch/fallback.
- **Mức độ:** Medium
- **Ảnh hưởng:** Biểu đồ có thể trắng và không báo rõ lỗi trong mạng/offline.
- **TASK liên quan:** TASK-06

### T06-009 – Validation status chưa đầy đủ cho các trạng thái trung gian

- **File:** `Services/TaskLifecycle.cs:13-26`
- **Hiện trạng:** `Assigned/PendingInspection/NeedsRework/Cancelled` chỉ cần 0–100; không có rule riêng đối với 100 và inspection cho các status này.
- **Mức độ:** High
- **Ảnh hưởng:** Có thể lưu tổ hợp trạng thái không hợp lệ nếu chưa đủ inspection.
- **TASK liên quan:** TASK-05, TASK-06, TASK-11

### T06-010 – Không có history UI

- **File:** `Views/Progress/Index.cshtml`; `Models/TaskProgressHistory.cs`
- **Hiện trạng:** History chỉ có entity/test/seed; không có màn hình xem lịch sử.
- **Mức độ:** Medium
- **Ảnh hưởng:** Người dùng không thể xem timeline trực tiếp.
- **TASK liên quan:** TASK-06

### T06-011 – Comment/Cập nhật trao đổi chưa triển khai

- **File:** Không tìm thấy model/controller/view tương ứng
- **Hiện trạng:** Không có TaskComment/Update entity hoặc CRUD.
- **Mức độ:** Medium
- **Ảnh hưởng:** Không đáp ứng yêu cầu trao đổi/cập nhật thông tin.
- **TASK liên quan:** TASK-05, TASK-06

### T06-012 – Cancel không ghi lý do bắt buộc

- **File:** `Models/Task.cs`; `Models/TaskProgressHistory.cs:5-28`; `TaskLifecycle.cs`
- **Hiện trạng:** `DelayReason` chỉ dành lý do chậm; không có CancelReason.
- **Mức độ:** Medium
- **Ảnh hưởng:** Lý do hủy không được đảm bảo và không audit riêng.
- **TASK liên quan:** TASK-05, TASK-06

## PHẦN 9 – ĐÁNH GIÁ RỦI RO

| Mức độ | Vấn đề |
|---|---|
| Critical | Không phát hiện lỗ hổng quyền truy cập nghiêm trọng; phạm vi và read-only có kiểm tra core |
| High | Workflow thiếu; dependency chưa thực thi; audit chưa đầy đủ; concurrency chưa có; validation trung gian cần rà thêm |
| Medium | Create history NEEDS_VERIFICATION; Progress pagination/filter; chart offline; history UI; comment/cancel reason |
| Low | Việc không có report/export riêng là thiếu theo yêu cầu mở rộng, chưa chứng minh là bug trong yêu cầu hiện tại |

## PHẦN 10 – ĐỀ XUẤT KẾ HOẠCH HOÀN THIỆN

Không triển khai trong giai đoạn audit. Thứ tự đề xuất:

1. Xác nhận yêu cầu workflow Draft/Submitted/Approved/adjustment/cancel.
2. Viết test SQLite cho Task create history trước khi xác minh T06-005.
3. Chốt state machine và validation riêng cho mọi status/%, inspection.
4. Bổ sung audit toàn diện và cơ chế xem lịch sử.
5. Thêm optimistic concurrency và test cập nhật đồng thời.
6. Thiết kế và triển khai TaskDependency sau khi chốt predecessor rule và blocker behavior.
7. Bổ sung comment/notification nếu thuộc phạm vi TASK-06.
8. Bổ sung filter/pagination và cải thiện ChartData/error handling.
9. Bổ sung cancel reason và approved adjustment workflow.
10. Viết test integration/UI cho các gap, không chạy trên database thật.

## PHẦN 11 – KẾT LUẬN

TASK-06 hiện có phần nền tiến độ thực tế: xem Task theo scope, cập nhật phần trăm/trạng thái, lịch sử trước-sau và mốc ngày thực tế, biểu đồ và kiểm soát read-only. Các phần này có thể chạy trên backend và được giao diện tham chiếu.

Những phần thực sự hoạt động trên source:

- Progress GET/POST/ChartData;
- scope và read-only;
- Worker/SE chỉ update khi được assignment;
- 8-role matrix ở service/test;
- history status/%;
- Quality Passed/Failed tác động Task;
- Completion/dashboard lấy Task theo scope.

Những phần còn thiếu:

- state machine Draft/Submitted/Approved;
- approval/change adjustment;
- TaskDependency enforcement;
- comment/discussion;
- lịch sử toàn diện;
- giao diện lịch sử;
- concurrency;
- pagination/filter/report;
- cancel reason và cancel workflow.

Trước khi chuyển sang giai đoạn hoàn thiện, cần xác nhận:

- TASK-06 có giữ workflow status hiện tại hay áp dụng workflow Draft/Submitted/Approved nói trong audit;
- TaskDependency có bắt buộc hay chỉ là entity dự phòng;
- Comment và audit đầy đủ có nằm trong TASK-06;
- Quy tắc cho trạng thái Assigned/PendingInspection/NeedsRework/Cancelled tại từng mức %.

Các thành phần tuyệt đối không nên thay đổi tự do:

- `TaskAccess.LoadAsync`;
- `TaskLifecycle.Apply/ValidateAsync`;
- `TaskService.ValidateAsync`;
- quan hệ Task–Project–WBS;
- quan hệ Task–Inspection;
- unique assignment và scope read-only.

Đã kiểm tra file/test chủ yếu liên quan nhưng chưa chạy test để tránh ghi database. Không có migration/database/source nghiệp vụ được tạo hoặc cập nhật trong lượt rà soát này; chỉ tạo báo cáo audit dưới `docs/TASK-06/`.
