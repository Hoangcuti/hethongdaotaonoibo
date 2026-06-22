// ============================================================
// HR DASHBOARD - ADVANCED COURSE BUILDER & DOCUMENT LIBRARY JS
// ============================================================

// Global variables for HR Builder
let hrDocumentLibraryData = { courses: [], modules: [], lessons: [], exams: [], questions: [] };
let hrCurrentLibraryTab = 'modules';
let hrLastGeneratedQuestions = [];
let hrIsLessonVideoDeleted = false;
let hrAllQuestionPoolData = [];
let hrCurrentQuestionTypeFilter = 'all';
let hrCurrentContentCourseId = null;
let hrCurrentModuleId = null;
let hrCurrentCourseContentParams = { modules: [], exams: [] };
let hrBuilderActiveExamQuestions = [];
let hrBuilderExamId = null;
let hrLoadedExamsList = [];
let hrExamDragData = null;
let hrDragData = null;
let hrLoadedDepartmentsList = [];
let hrCurrentPreviewModuleId = null;
let hrCurrentPreviewCourseId = null;

// Helper functions
function hrLibraryEscape(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function hrFmtDate(dateString) {
    if (!dateString) return '--';
    try {
        let d;
        if (dateString instanceof Date) {
            d = dateString;
        } else {
            let clean = String(dateString);
            if (!clean.includes('T') && clean.match(/^\d{4}-\d{2}-\d{2}$/)) {
                clean += 'T00:00:00';
            }
            const cleanDateStr = (clean.includes('Z') || clean.match(/[+-]\d{2}:\d{2}$/)) ? clean : clean + '+07:00';
            d = new Date(cleanDateStr);
        }
        return d.toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Ho_Chi_Minh' });
    } catch (e) {
        return dateString;
    }
}

// ============================================================
// 1. DOCUMENT LIBRARY MANAGEMENT
// ============================================================
async function loadDocumentLibrary() {
    try {
        const data = await apiFetch('/api/hr/content-library');
        hrDocumentLibraryData = {
            courses: data.courses || [],
            modules: data.modules || [],
            lessons: data.lessons || [],
            exams: data.exams || [],
            questions: data.questions || []
        };
        hrAllQuestionPoolData = hrDocumentLibraryData.questions || [];
        
        // Refresh department list if empty
        if (!hrLoadedDepartmentsList || !hrLoadedDepartmentsList.length) {
            const depts = await apiFetch('/api/hr/departments');
            hrLoadedDepartmentsList = depts;
        }
        renderDocumentLibraryFilters();
        renderDocumentLibraryStats();
        renderDocumentLibrary();
    } catch (e) {
        const head = document.getElementById('documentLibraryHead');
        const body = document.getElementById('documentLibraryTable');
        if (head) head.innerHTML = '';
        if (body) body.innerHTML = `<tr><td colspan="6" style="text-align:center;color:#ef4444;padding:24px">Không tải được kho tài liệu: ${hrLibraryEscape(e.message)}</td></tr>`;
    }
}

function renderDocumentLibraryFilters() {
    const deptFilter = document.getElementById('libraryDeptFilter');
    if (!deptFilter) return;
    const currentValue = deptFilter.value;
    deptFilter.innerHTML = '<option value="">Tất cả phòng ban</option>' + (hrLoadedDepartmentsList || []).map(d => `
        <option value="${d.departmentId}">${hrLibraryEscape(d.departmentName || d.name)}</option>
    `).join('');
    deptFilter.value = currentValue;
    
    const courseFilter = document.getElementById('libraryCourseFilter');
    if (courseFilter) {
        const currentCourse = courseFilter.value;
        courseFilter.innerHTML = '<option value="">Tất cả khóa học</option>' + (hrDocumentLibraryData.courses || []).map(c => {
            const code = c.courseCode || c.CourseCode || '';
            return `<option value="${c.id || c.courseId}">${hrLibraryEscape(c.title || c.courseName)}${code ? ` (${code})` : ''}</option>`;
        }).join('');
        courseFilter.value = currentCourse;
    }
}

function renderDocumentLibraryStats() {
    const attachmentCount = (hrDocumentLibraryData.lessons || []).reduce((sum, lesson) => sum + (lesson.attachmentsCount || 0), 0);
    const mCount = document.getElementById('libraryModulesCount');
    const lCount = document.getElementById('libraryLessonsCount');
    const eCount = document.getElementById('libraryExamsCount');
    const aCount = document.getElementById('libraryAttachmentsCount');
    
    if (mCount) mCount.textContent = hrDocumentLibraryData.modules.length;
    if (lCount) lCount.textContent = hrDocumentLibraryData.lessons.length;
    if (eCount) eCount.textContent = hrDocumentLibraryData.exams.length;
    if (aCount) aCount.textContent = attachmentCount;
}

function switchLibraryTab(tab) {
    hrCurrentLibraryTab = tab;
    const tabs = {
        modules: ['libraryTabModules', 'btn btn-primary'],
        lessons: ['libraryTabLessons', 'btn btn-primary'],
        exams: ['libraryTabExams', 'btn btn-primary'],
        questions: ['libraryTabQuestions', 'btn btn-primary']
    };
    Object.entries(tabs).forEach(([key, [id, activeClass]]) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.className = key === tab ? activeClass : 'btn btn-secondary';
    });

    const subTabsEl = document.getElementById('questionsSubTabs');
    if (subTabsEl) {
        subTabsEl.style.display = tab === 'questions' ? 'flex' : 'none';
    }

    renderDocumentLibrary();
}

function switchQuestionSubTab(type) {
    hrCurrentQuestionTypeFilter = type;
    const subTabs = {
        all: 'qSubTabAll',
        MultipleChoice: 'qSubTabMC',
        Essay: 'qSubTabEssay',
        FillInTheBlank: 'qSubTabFITB'
    };
    Object.entries(subTabs).forEach(([key, id]) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.className = key === type ? 'btn btn-primary btn-sm question-sub-tab' : 'btn btn-secondary btn-sm question-sub-tab';
    });
    renderDocumentLibrary();
}

function getFilteredLibraryRows() {
    const keyword = (document.getElementById('librarySearch')?.value || '').trim().toLowerCase();
    const deptId = document.getElementById('libraryDeptFilter')?.value || '';
    const courseId = document.getElementById('libraryCourseFilter')?.value || '';
    let rows = hrDocumentLibraryData[hrCurrentLibraryTab] || [];
    
    if (hrCurrentLibraryTab === 'questions') {
        if (hrCurrentQuestionTypeFilter !== 'all') {
            rows = rows.filter(q => q.questionType === hrCurrentQuestionTypeFilter);
        }
    }
    
    return rows.filter(row => {
        if (hrCurrentLibraryTab !== 'questions') {
            const rowDept = String(row.targetDepartmentId || row.ownerDeptId || row.departmentId || '');
            const matchesDept = !deptId || rowDept === deptId;
            const matchesCourse = !courseId || String(row.courseId || row.ownerCourseId || '') === courseId;
            if (!matchesDept || !matchesCourse) return false;
        }
        
        if (!keyword) return true;

        return [
            row.title,
            row.examTitle,
            row.courseTitle,
            row.courseCode,
            row.moduleTitle,
            row.contentType,
            row.questionText
        ].some(value => String(value || '').toLowerCase().includes(keyword));
    });
}

function renderDocumentLibrary() {
    const createBtn = document.getElementById('libraryCreateBtn');
    if (createBtn) {
        createBtn.textContent = hrCurrentLibraryTab === 'modules' ? '➕ Tạo Chương Mới' : hrCurrentLibraryTab === 'lessons' ? '➕ Tạo Bài Học Mới' : hrCurrentLibraryTab === 'exams' ? '➕ Tạo Quiz Mới' : '➕ Tạo Câu Hỏi Mới';
    }

    const rows = getFilteredLibraryRows();
    const head = document.getElementById('documentLibraryHead');
    const body = document.getElementById('documentLibraryTable');
    if (!head || !body) return;

    if (hrCurrentLibraryTab === 'modules') {
        head.innerHTML = '<tr><th>ID</th><th>Tên chương</th><th>Level</th><th>Sử dụng trong</th><th>Bài học</th><th>Thao tác</th></tr>';
        body.innerHTML = rows.length ? rows.map(row => `
            <tr>
                <td>${row.moduleId}</td>
                <td><strong>${hrLibraryEscape(row.title)}</strong></td>
                <td>${row.level ? `<span class="badge badge-info">Level ${row.level}</span>` : '<span style="color:#94a3b8">--</span>'}</td>
                <td>${row.courseTitle ? `<span class="badge badge-blue" style="background:#eff6ff; color:#1d4ed8; border:1px solid #bfdbfe">${hrLibraryEscape(row.courseTitle)}${row.courseCode || row.CourseCode ? ` (${row.courseCode || row.CourseCode})` : ''}</span>` : (row.deptName || row.categoryName || '<span style="color:#94a3b8">Hệ thống</span>')}</td>
                <td>${row.lessonsCount || 0}</td>
                <td>
                    <div style="display:flex;gap:8px;justify-content:flex-end">
                        <button class="btn btn-info btn-sm" style="background:#3b82f6;color:white;border:none" onclick="previewModuleLessons(${row.moduleId})">Xem</button>
                        <button class="btn btn-secondary btn-sm" onclick="openEditModuleModal(${row.moduleId})">Sửa</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteModule(${row.moduleId})">Xóa</button>
                    </div>
                </td>
            </tr>
        `).join('') : '<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Chương học trống.</td></tr>';
        return;
    }

    if (hrCurrentLibraryTab === 'lessons') {
        head.innerHTML = '<tr><th>ID</th><th>Tên bài học</th><th>Thuộc chương</th><th>Khóa học</th><th>Tài liệu</th><th>Thao tác</th></tr>';
        body.innerHTML = rows.length ? rows.map(row => {
            const attachments = row.attachmentsCount ? `<span class="badge badge-blue">${row.attachmentsCount} tài liệu</span>` : '<span style="color:#94a3b8">Chưa có</span>';
            const video = row.videoUrl ? '<span class="badge badge-info">Video</span>' : '';
            const type = row.contentType ? `<span class="badge badge-purple">${hrLibraryEscape(row.contentType)}</span>` : '';
            return `
            <tr>
                <td>${row.lessonId}</td>
                <td>
                    <strong>${hrLibraryEscape(row.title)}</strong>
                    <div style="margin-top:6px;display:flex;gap:6px;flex-wrap:wrap">
                        ${row.level ? `<span class="badge badge-info">Level ${row.level}</span>` : ''}
                        ${type}
                        ${video}
                    </div>
                </td>
                <td>${hrLibraryEscape(row.moduleTitle || 'Chưa gán')}</td>
                <td><span class="badge badge-blue">${hrLibraryEscape(row.courseTitle || 'Chưa gán khóa học')}${row.courseCode || row.CourseCode ? ` (${row.courseCode || row.CourseCode})` : ''}</span></td>
                <td>${attachments}</td>
                <td>
                    <div style="display:flex;gap:8px;justify-content:flex-end">
                        <button class="btn btn-info btn-sm" style="background:#3b82f6;color:white;border:none" onclick="previewLessonContent(${row.lessonId})">Xem</button>
                        <button class="btn btn-secondary btn-sm" onclick="openEditLessonModal(${row.lessonId})">Sửa</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteLesson(${row.lessonId})">Xóa</button>
                    </div>
                </td>
            </tr>`;
        }).join('') : '<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Chưa có bài học nào.</td></tr>';
        return;
    }

    if (hrCurrentLibraryTab === 'questions') {
        head.innerHTML = '<tr><th style="width:50px">ID</th><th>Nội dung câu hỏi</th><th style="width:150px">Phân loại</th><th style="width:100px">Độ khó</th><th style="width:240px; text-align:right">Thao tác</th></tr>';
        body.innerHTML = rows.length ? rows.map(row => {
            let optionsText = '';
            if (row.questionType === 'MultipleChoice' && row.options) {
                optionsText = `<div style="margin-top:6px; font-size:12px; color:#475569">
                    ${row.options.map(o => `<span style="margin-right:12px; padding:2px 6px; border-radius:4px; background:${o.isCorrect ? '#dcfce7; color:#166534' : '#f1f5f9; color:#475569'}">${o.isCorrect ? '✓ ' : ''}${hrLibraryEscape(o.optionText)}</span>`).join('')}
                </div>`;
            } else if (row.questionType === 'FillInTheBlank' && row.options) {
                const correctAnswers = row.options.filter(o => o.isCorrect).map(o => o.optionText);
                optionsText = `<div style="margin-top:6px; font-size:12px; color:#4a044e">
                    <strong>Từ khóa đúng:</strong> <span style="background:#fdf4ff; border:1px dashed #e879f9; padding:2px 6px; border-radius:4px">${hrLibraryEscape(correctAnswers.join(' / '))}</span>
                </div>`;
            } else if (row.questionType === 'Essay') {
                optionsText = `<div style="margin-top:6px; font-size:12px; color:#475569">
                    <em>(Học viên sẽ nhập văn bản tự luận khi làm bài)</em>
                </div>`;
            }

            const typeLabel = row.questionType === 'MultipleChoice' ? 'Trắc nghiệm' : row.questionType === 'Essay' ? 'Tự luận' : 'Điền từ';
            const typeIcon = row.questionType === 'MultipleChoice' ? '☑️' : row.questionType === 'Essay' ? '✍️' : '✏️';

            return `
            <tr>
                <td style="color:#64748b; font-family:monospace">${row.questionId}</td>
                <td>
                    <strong>${hrLibraryEscape(row.questionText)}</strong>
                    ${optionsText}
                </td>
                <td><span class="badge badge-info">${typeIcon} ${typeLabel}</span></td>
                <td><span class="badge badge-secondary">${row.difficulty || 'Medium'}</span></td>
                <td>
                    <div style="display:flex; gap:8px; justify-content:flex-end">
                        <button class="btn btn-secondary btn-sm" onclick="openEditQuestionModal(${row.questionId})">Sửa</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteQuestion(${row.questionId})">Xóa</button>
                    </div>
                </td>
            </tr>`;
        }).join('') : '<tr><td colspan="5" style="text-align:center;color:#94a3b8;padding:24px">Chưa có câu hỏi nào. Quý khách vui lòng nhấn "Tạo Câu Hỏi Mới" ở trên.</td></tr>';
        return;
    }

    if (hrCurrentLibraryTab === 'exams') {
        head.innerHTML = '<tr><th style="width:50px">ID</th><th>Tiêu đề Quiz</th><th style="width:100px">Level</th><th>Sử dụng trong</th><th style="width:200px">Cấu hình</th><th style="width:240px; text-align:right">Thao tác</th></tr>';
        body.innerHTML = rows.length ? rows.map(row => `
            <tr>
                <td style="color:#64748b; font-family:monospace">${row.examId}</td>
                <td>
                    <div style="font-weight:700; color:#1e293b">${hrLibraryEscape(row.examTitle)}</div>
                    <div style="font-size:11px; color:#64748b; margin-top:2px">Cập nhật: ${hrFmtDate(row.createdAt)}</div>
                </td>
                <td>${row.level ? `<span class="badge badge-info" style="background:#e0f2fe; color:#0369a1; border:1px solid #bae6fd">Level ${row.level}</span>` : '<span style="color:#94a3b8">--</span>'}</td>
                <td><span class="badge" style="background:#fff7ed; color:#c2410c; border:1px solid #ffedd5">${hrLibraryEscape(row.courseTitle || 'Chưa gán')}${row.courseCode || row.CourseCode ? ` (${row.courseCode || row.CourseCode})` : ''}</span></td>
                <td>
                    <div style="display:flex;gap:4px;flex-wrap:wrap">
                        <span class="badge badge-info" style="font-size:10px" title="Thời gian">${row.durationMinutes || 0} phút</span>
                        <span class="badge badge-purple" style="font-size:10px" title="Điểm đỗ">Đỗ ${row.passScore || 0}</span>
                        <span class="badge badge-blue" style="font-size:10px; cursor:pointer" onclick="openExamQuestionsManagementModal(${row.examId})" title="Quản lý câu hỏi">📂 ${row.questionsCount || 0} câu</span>
                    </div>
                </td>
                <td>
                    <div style="display:flex;gap:6px;justify-content:flex-end;align-items:center;">
                        <button class="btn btn-sm" style="background: linear-gradient(135deg, #fef3c7, #fde68a); color:#92400e; border:1px solid #fcd34d; font-weight:800; padding:5px 10px; font-size:11px; box-shadow: 0 1px 2px rgba(0,0,0,0.05)" onclick="suggestMultipleQuestionsAI(${row.examId})" title="AI Gợi ý bộ đề">🚀 AI</button>
                        <button class="btn btn-primary btn-sm" style="padding:5px 12px; background:#2563eb;" onclick="openExamQuestionsManagementModal(${row.examId})" title="Thiết kế câu hỏi">➕ Tạo</button>
                        <button class="btn btn-secondary btn-sm" style="padding:5px 12px;" onclick="openEditExamModal(${row.examId})" title="Sửa thông tin">📝</button>
                        <button class="btn btn-danger btn-sm" style="padding:5px 12px;" onclick="deleteExam(${row.examId})" title="Xóa Quiz">🗑️</button>
                    </div>
                </td>
            </tr>
        `).join('') : '<tr><td colspan="6" style="text-align:center;color:#94a3b8;padding:24px">Chưa có quiz nào. Quý khách vui lòng nhấn "Tạo Quiz Mới" ở trên.</td></tr>';
    }
}

function openLibraryCreateModal() {
    try {
        if (hrCurrentLibraryTab === 'modules') {
            const courseSelect = document.getElementById('libraryModuleCourseInput');
            if (courseSelect) {
                const options = (hrDocumentLibraryData.courses || []).map(c => 
                    `<option value="${c.courseId}">${hrLibraryEscape(c.title)}</option>`
                ).join('');
                courseSelect.innerHTML = '<option value="">-- Chọn khóa học --</option>' + options;
                
                const filterVal = document.getElementById('libraryCourseFilter').value;
                if (filterVal) courseSelect.value = filterVal;
            }

            const select = document.getElementById('libraryModuleDeptInput');
            if (select) {
                const options = (hrLoadedDepartmentsList || []).map(d => 
                    `<option value="${d.departmentId}">${hrLibraryEscape(d.departmentName)}</option>`
                ).join('');
                select.innerHTML = options || '<option value="">(Không có phòng ban)</option>';
            }
            
            const mId = document.getElementById('libraryModuleId'); if (mId) mId.value = '';
            const mTitle = document.getElementById('libraryModuleTitleInput'); if (mTitle) mTitle.value = '';
            
            openModal('libraryModuleModal');
            return;
        }

        if (hrCurrentLibraryTab === 'lessons') {
            const select = document.getElementById('libraryLessonModuleInput');
            if (select) {
                const options = (hrDocumentLibraryData.modules || []).map(m => {
                    const code = m.courseCode || m.CourseCode || '';
                    return `<option value="${m.moduleId}">${hrLibraryEscape(m.title)}${code ? ` (${code})` : ''}</option>`;
                }).join('');
                select.innerHTML = options || '<option value="">(Không có chương)</option>';
            }
            
            const lId = document.getElementById('libraryLessonId'); if (lId) lId.value = '';
            const lTitle = document.getElementById('libraryLessonTitleInput'); if (lTitle) lTitle.value = '';
            
            openModal('libraryLessonModal');
            return;
        }

        if (hrCurrentLibraryTab === 'exams') {
            const eId = document.getElementById('libraryExamId'); if (eId) eId.value = '';
            const eTitle = document.getElementById('libraryExamTitleInput'); if (eTitle) eTitle.value = '';
            const eLevel = document.getElementById('libraryExamLevelInput'); if (eLevel) eLevel.value = '';
            const eDuration = document.getElementById('libraryExamDurationInput'); if (eDuration) eDuration.value = 30;
            const ePass = document.getElementById('libraryExamPassScoreInput'); if (ePass) ePass.value = 50;
            const eMax = document.getElementById('libraryExamMaxAttemptsInput'); if (eMax) eMax.value = '';
            const eStart = document.getElementById('libraryExamStartDateInput'); if (eStart) eStart.value = '';
            const eEnd = document.getElementById('libraryExamEndDateInput'); if (eEnd) eEnd.value = '';
            
            const deptInput = document.getElementById('libraryExamTargetDeptInput');
            if (deptInput) {
                const deptOpts = '<option value="">-- Tất cả phòng ban --</option>' + (hrLoadedDepartmentsList || []).map(d => `<option value="${d.departmentId}">${d.departmentName}</option>`).join('');
                deptInput.innerHTML = deptOpts;
            }
            
            openModal('libraryExamModal');
            return;
        }

        if (hrCurrentLibraryTab === 'questions') {
            openCreateQuestionModal();
            return;
        }
    } catch (e) {
        showToast('Lỗi khi mở bảng thêm mới: ' + e.message, 'error');
    }
}

function fillLibraryCourseOptions(elementId) {
    const select = document.getElementById(elementId);
    if (!select) return;
    select.innerHTML = '<option value="">-- Chọn khóa học --</option>' + hrDocumentLibraryData.courses.map(course => {
        const code = course.courseCode || course.CourseCode || '';
        return `<option value="${course.courseId}">${hrLibraryEscape(course.title || `Khóa học #${course.courseId}`)}${code ? ` (${code})` : ''}</option>`;
    }).join('');
}

function fillLibraryModuleOptions(elementId) {
    const select = document.getElementById(elementId);
    if (!select) return;
    select.innerHTML = '<option value="">-- Chọn chương --</option>' + hrDocumentLibraryData.modules.map(module => {
        const code = module.courseCode || module.CourseCode || '';
        return `<option value="${module.moduleId}">${hrLibraryEscape(module.title || `Chương #${module.moduleId}`)}${module.courseTitle ? ` - ${hrLibraryEscape(module.courseTitle)}` : ''}${code ? ` (${code})` : ''}</option>`;
    }).join('');
}

async function syncDocumentLibraryAfterCreate(options = {}) {
    const {
        tab = hrCurrentLibraryTab,
        courseId = '',
        refreshCourseContent = false
    } = options;

    await loadDocumentLibrary();
    hrCurrentLibraryTab = tab;

    const deptFilter = document.getElementById('libraryDeptFilter');
    if (deptFilter) {
        deptFilter.value = '';
    }

    renderDocumentLibrary();

    if (refreshCourseContent && hrCurrentContentCourseId && String(hrCurrentContentCourseId) === String(courseId)) {
        await loadBuilderLibrary();
        await loadCourseContent();
    }
}

async function submitLibraryModule() {
    const courseId = parseInt(document.getElementById('libraryModuleCourseInput').value) || 0;
    const deptId = document.getElementById('libraryModuleDeptInput').value;
    const title = document.getElementById('libraryModuleTitleInput').value.trim();
    const level = parseInt(document.getElementById('libraryModuleLevelInput').value) || null;
    
    if (!courseId) {
        showToast('Bạn phải chọn khóa học liên kết.', 'error');
        return;
    }
    if (!deptId || !title) {
        showToast('Bạn phải chọn phòng ban và nhập tên chương.', 'error');
        return;
    }

    try {
        await apiFetch(`/api/hr/documents`, {
            method: 'POST',
            body: JSON.stringify({
                title: title,
                courseId: courseId,
                newModuleName: title,
                targetType: 'module',
                pendingData: JSON.stringify({
                    title: title,
                    level: level,
                    targetDepartmentId: parseInt(deptId)
                })
            })
        });
        closeModal('libraryModuleModal');
        showToast('Đã gửi đề xuất tạo chương mới. Vui lòng chờ HR phòng đào tạo duyệt.', 'success');
        await loadDocumentLibrary();
        renderDocumentLibrary();
        if (typeof loadDeptDocuments === 'function') await loadDeptDocuments();
    } catch (e) {
        showToast(e.message || 'Lỗi gửi đề xuất', 'error');
    }
}

async function submitLibraryLesson() {
    const moduleId = document.getElementById('libraryLessonModuleInput').value;
    const title = document.getElementById('libraryLessonTitleInput').value.trim();
    const level = document.getElementById('libraryLessonLevelInput').value;
    
    if (!moduleId || !title) return showToast('Bạn phải chọn chương và nhập tên bài học.', 'error');

    const formData = new FormData();
    formData.append('title', title);
    if (level) formData.append('level', level);

    const activeTab = document.querySelector('.library-lesson-source-tab.active');
    const source = activeTab ? activeTab.getAttribute('data-source') : 'video';

    if (source === 'video') {
        formData.append('contentType', 'Video');
        const link = document.getElementById('libraryLessonVideoLinkInput').value.trim();
        const file = document.getElementById('libraryLessonVideoFileInput').files[0];
        if (file) formData.append('videoFile', file);
        if (link) formData.append('videoUrl', link);
    } else if (source === 'ai') {
        formData.append('contentType', 'Text');
        formData.append('contentBody', document.getElementById('libraryLessonBodyInput').value);
    }

    // Attachments
    const docFile = document.getElementById('libraryLessonDocFileInput').files[0];
    const docLink = document.getElementById('libraryLessonExternalDocLinkInput').value.trim();
    if (docFile) formData.append('attachmentFile', docFile);
    if (docLink) formData.append('attachmentUrl', docLink);

    try {
        await apiFetch(`/api/hr/modules/${moduleId}/lessons`, { method: 'POST', body: formData, isFormData: true });
        closeModal('libraryLessonModal');
        showToast('Tạo bài học thành công!');
        await loadDocumentLibrary();
    } catch(e) { showToast(e.message, 'error'); }
}

function switchLessonSource(source, prefix) {
    const containerPrefix = prefix === 'lesson' ? 'lesson' : (prefix === 'edit' ? 'editLesson' : 'libraryLesson');
    const tabClass = prefix === 'lesson' ? '.lesson-source-tab' : (prefix === 'edit' ? '.edit-lesson-source-tab' : '.library-lesson-source-tab');
    
    document.querySelectorAll(tabClass).forEach(btn => {
        if (btn.getAttribute('data-source') === source) {
            btn.classList.add('active');
            btn.style.background = '#6366f1';
            btn.style.color = '#fff';
        } else {
            btn.classList.remove('active');
            btn.style.background = 'transparent';
            btn.style.color = '#64748b';
        }
    });

    const sections = ['Video', 'Document', 'Link', 'AI'];
    sections.forEach(s => {
        const el = document.getElementById(`${containerPrefix}Source${s}`);
        if (el) el.style.display = (s.toLowerCase() === source.toLowerCase()) ? 'block' : 'none';
    });
}

async function generateLessonWithAI(prefix) {
    const promptId = prefix === 'library' ? 'aiLibraryLessonPrompt' : 'aiLessonPrompt';
    const statusId = prefix === 'library' ? 'aiLibraryLessonStatus' : 'aiLessonStatus';
    const btnId = prefix === 'library' ? 'btnGenerateLibraryLessonAI' : 'btnGenerateLessonAI';
    const bodyId = prefix === 'library' ? 'libraryLessonBodyInput' : 'lessonBodyInput';
    const titleId = prefix === 'library' ? 'libraryLessonTitleInput' : 'lessonTitleInput';

    const prompt = document.getElementById(promptId).value.trim();
    if (!prompt) { showToast('Vui lòng nhập chủ đề bài học!', 'warning'); return; }

    const statusEl = document.getElementById(statusId);
    const btn = document.getElementById(btnId);

    statusEl.innerHTML = '<span class="spinner-small"></span> Đang soạn thảo bài học...';
    btn.disabled = true;

    try {
        const data = await apiFetch('/api/hr/generate-lesson-ai', {
            method: 'POST',
            body: JSON.stringify({ prompt: prompt })
        });
        
        if (data.title) {
            document.getElementById(titleId).value = data.title;
        }
        document.getElementById(bodyId).value = data.contentBody;
        statusEl.innerHTML = `<span style="color:#10b981">✨ AI đã soạn thảo xong. Bạn có thể xem và chỉnh sửa ở ô Nội dung bài học.</span>`;
        showToast('AI đã soạn thảo xong!');
    } catch (e) {
        statusEl.innerHTML = `<span style="color:#ef4444">Lỗi AI: ${e.message}</span>`;
    } finally {
        btn.disabled = false;
    }
}

async function submitLibraryExam() {
    const btn = event?.target || document.querySelector('#libraryExamModal .btn-primary');
    const originalText = btn ? btn.innerHTML : '💾 Lưu';
    
    try {
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-small"></span> Đang xử lý...';
        }

        const titleEl = document.getElementById('libraryExamTitleInput');
        const levelEl = document.getElementById('libraryExamLevelInput');
        const durationEl = document.getElementById('libraryExamDurationInput');
        const passScoreEl = document.getElementById('libraryExamPassScoreInput');
        const maxAttemptsEl = document.getElementById('libraryExamMaxAttemptsInput');
        const startEl = document.getElementById('libraryExamStartDateInput');
        const endEl = document.getElementById('libraryExamEndDateInput');
        const deptEl = document.getElementById('libraryExamTargetDeptInput');

        if (!titleEl) throw new Error('Element libraryExamTitleInput not found in DOM');

        const examTitle = titleEl.value.trim();
        const level = levelEl && levelEl.value ? parseInt(levelEl.value) : null;
        const durationMinutes = durationEl && durationEl.value ? parseInt(durationEl.value) : 30;
        const passScore = passScoreEl && passScoreEl.value ? parseFloat(passScoreEl.value) : 50;
        const maxAttempts = maxAttemptsEl && maxAttemptsEl.value ? parseInt(maxAttemptsEl.value) : null;
        const startDate = startEl && startEl.value ? startEl.value : null;
        const endDate = endEl && endEl.value ? endEl.value : null;
        const targetDepartmentId = deptEl && deptEl.value ? parseInt(deptEl.value) : null;

        if (!examTitle) {
            showToast('Bạn phải nhập tiêu đề quiz.', 'warning');
            if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
            return;
        }
        if (passScore < 0) {
            showToast('Điểm của quiz không được âm.', 'warning');
            if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
            return;
        }
        if (maxAttempts !== null && maxAttempts < 0) {
            showToast('Số lần làm bài không được âm.', 'warning');
            if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
            return;
        }
        if (durationMinutes < 0) {
            showToast('Thời gian làm bài không được âm.', 'warning');
            if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
            return;
        }
        const now = new Date();
        const minTime = new Date(now.getTime() - 5 * 60 * 1000);
        if (startDate) {
            if (new Date(startDate) < minTime) {
                showToast('Ngày bắt đầu không được ở trong quá khứ.', 'warning');
                if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
                return;
            }
        }
        if (endDate) {
            if (new Date(endDate) < minTime) {
                showToast('Ngày kết thúc không được ở trong quá khứ.', 'warning');
                if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
                return;
            }
        }
        if (startDate && endDate) {
            if (new Date(endDate) < new Date(startDate)) {
                showToast('Ngày kết thúc không được trước ngày bắt đầu.', 'warning');
                if (btn) { btn.disabled = false; btn.innerHTML = originalText; }
                return;
            }
        }

        const payload = { 
            examTitle, 
            level, 
            durationMinutes, 
            passScore, 
            maxAttempts, 
            startDate, 
            endDate, 
            targetDepartmentId,
            aiQuestions: hrLastGeneratedQuestions || []
        };

        const result = await apiFetch(`/api/hr/courses/0/exams`, {
            method: 'POST',
            body: JSON.stringify(payload)
        });

        hrLastGeneratedQuestions = [];
        closeModal('libraryExamModal');
        showToast('Đã lưu bài quiz mới thành công.', 'success');
        
        await syncDocumentLibraryAfterCreate({
            tab: 'exams',
            courseId: 0,
            refreshCourseContent: true
        });

        if (typeof loadExamsPageList === 'function') {
            await loadExamsPageList();
        }
    } catch (e) {
        showToast('Lỗi khi lưu bài quiz: ' + (e.message || 'Lỗi không xác định'), 'error');
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = originalText;
        }
    }
}

async function generateQuizWithAI(source = 'library') {
    const prompt = document.getElementById(source === 'library' ? 'aiQuizPrompt' : 'aiExamPrompt').value;
    const statusDiv = document.getElementById(source === 'library' ? 'aiQuizStatus' : 'aiExamStatus');
    const btn = document.getElementById(source === 'library' ? 'btnGenerateQuizAI' : 'btnGenerateExamAI');
    
    if (!prompt) {
        showToast('Vui lòng nhập yêu cầu cho AI', 'warning');
        return;
    }
    
    btn.disabled = true;
    btn.innerText = 'Đang xử lý...';
    statusDiv.innerText = '⏳ AI đang thiết kế bộ câu hỏi...';
    
    try {
        const result = await apiFetch('/api/hr/generate-quiz-ai', {
            method: 'POST',
            body: JSON.stringify({ prompt })
        });

        if (result && result.examTitle) {
            document.getElementById(source === 'library' ? 'libraryExamTitleInput' : 'examTitleInput').value = result.examTitle;
            if (result.questions) {
                hrLastGeneratedQuestions = result.questions.map(q => ({
                    questionText: q.questionText,
                    questionType: q.questionType || q.QuestionType || 'MultipleChoice',
                    points: q.points || 10,
                    options: (q.options || []).map(opt => {
                        if (typeof opt === 'string') return { optionText: opt, isCorrect: false };
                        return {
                            optionText: opt.optionText || opt.OptionText || '',
                            isCorrect: opt.isCorrect !== undefined ? opt.isCorrect : (opt.IsCorrect || false)
                        };
                    })
                }));
                statusDiv.innerText = `✅ Đã soạn ${hrLastGeneratedQuestions.length} câu hỏi.`;
                showToast('AI đã soạn thảo xong!');
            }
        }
    } catch(e) {
        showToast('Lỗi AI: ' + e.message, 'error');
    } finally {
        btn.disabled = false;
        btn.innerText = 'Tạo từ Text';
    }
}

async function generateQuizFromFile(source = 'exam') {
    const fileInput = document.getElementById(source === 'exam' ? 'examFileAI' : 'libraryExamFileAI');
    const statusDiv = document.getElementById(source === 'exam' ? 'aiExamStatus' : 'aiQuizStatus');
    
    if (!fileInput.files || fileInput.files.length === 0) {
        showToast('Vui lòng chọn file document (PDF, Docx, TXT...)', 'warning');
        return;
    }

    const file = fileInput.files[0];
    statusDiv.innerText = '⏳ Đang đọc file và trích xuất câu hỏi...';
    statusDiv.style.color = '#f59e0b';

    try {
        const base64Full = await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = reject;
            reader.readAsDataURL(file);
        });
        
        const pureBase64 = base64Full.split(',')[1];
        
        const result = await apiFetch('/api/hr/generate-quiz-from-file', {
            method: 'POST',
            body: JSON.stringify({
                base64Data: pureBase64,
                mimeType: file.type || 'application/pdf'
            })
        });

        if (result && result.questions) {
            hrLastGeneratedQuestions = result.questions.map(q => ({
                questionText: q.questionText,
                questionType: q.questionType || q.QuestionType || 'MultipleChoice',
                points: q.points || 10,
                options: (q.options || []).map(opt => {
                    if (typeof opt === 'string') return { optionText: opt, isCorrect: false };
                    return {
                        optionText: opt.optionText || opt.OptionText || '',
                        isCorrect: opt.isCorrect !== undefined ? opt.isCorrect : (opt.IsCorrect || false)
                    };
                })
            }));
            
            statusDiv.innerText = `✅ Đã trích xuất ${hrLastGeneratedQuestions.length} câu hỏi thành công!`;
            statusDiv.style.color = '#10b981';
            showToast('Đã trích xuất câu hỏi từ file!');

            // Tự động lưu bài quiz ngay sau khi trích xuất thành công
            showToast('Đang tự động lưu bài thi...', 'info');
            const cleanFileName = file.name.substring(0, file.name.lastIndexOf('.')) || file.name;
            const payload = { 
                examTitle: result.examTitle || `Bài thi từ file: ${cleanFileName}`, 
                level: null, 
                durationMinutes: result.durationMinutes || 30, 
                passScore: 50, 
                maxAttempts: 1, 
                startDate: null, 
                endDate: null, 
                targetDepartmentId: null,
                aiQuestions: hrLastGeneratedQuestions
            };

            const saveResult = await apiFetch(`/api/hr/courses/0/exams`, {
                method: 'POST',
                body: JSON.stringify(payload)
            });

            if (saveResult && saveResult.examId) {
                showToast('Đã tự động lưu bài quiz mới thành công!', 'success');
                hrLastGeneratedQuestions = [];
                closeModal(source === 'exam' ? 'examModal' : 'libraryExamModal');
                
                await syncDocumentLibraryAfterCreate({
                    tab: 'exams',
                    courseId: 0,
                    refreshCourseContent: true
                });

                if (typeof loadExamsPageList === 'function') {
                    await loadExamsPageList();
                }

                // Tự động mở modal Quản lý câu hỏi để người dùng xem luôn
                await openExamQuestionsManagementModal(saveResult.examId);
            }
        }
    } catch(e) {
        statusDiv.innerText = '❌ Lỗi: ' + e.message;
        statusDiv.style.color = '#ef4444';
        showToast('Lỗi xử lý file AI', 'error');
    }
}

async function generateModuleWithAI(source) {
    const promptId = source === 'library' ? 'aiLibraryModulePrompt' : 'aiModulePrompt';
    const statusId = source === 'library' ? 'aiLibraryModuleStatus' : 'aiModuleStatus';
    const btnId = source === 'library' ? 'btnGenerateLibraryModuleAI' : 'btnGenerateModuleAI';
    const titleId = source === 'library' ? 'libraryModuleTitleInput' : 'moduleTitleInput';

    const prompt = document.getElementById(promptId).value.trim();
    if (!prompt) { showToast('Vui lòng nhập chủ đề chương!', 'warning'); return; }

    const statusEl = document.getElementById(statusId);
    const btn = document.getElementById(btnId);

    statusEl.innerHTML = '<span class="spinner-small"></span> Đang phân tích chủ đề...';
    btn.disabled = true;

    try {
        const data = await apiFetch('/api/hr/generate-module-ai', {
            method: 'POST',
            body: JSON.stringify({ prompt: prompt })
        });
        
        if (data.title) {
            document.getElementById(titleId).value = data.title;
        }
        statusEl.innerHTML = `<span style="color:#10b981">✨ AI đã đề xuất chương: "${data.title || prompt}". Quý khách có thể sửa và lưu lại.</span>`;
        showToast('AI đã tạo chủ đề thành công!');
    } catch (e) {
        statusEl.innerHTML = `<span style="color:#ef4444">Lỗi AI: ${e.message}</span>`;
    } finally {
        btn.disabled = false;
    }
}


// ============================================================
// 2. ADVANCED COURSE BUILDER (DRAG & DROP)
// ============================================================
let hrCourseContentReadOnly = false;

async function openCourseContentModal(courseId, readOnly = false) {
    hrCurrentContentCourseId = courseId;
    hrCurrentModuleId = null;
    hrCourseContentReadOnly = !!readOnly;
    document.getElementById('contentCourseId').value = courseId;
    
    // Find course details
    const course = courses.find(c => (c.courseId || c.id) == courseId);
    if (course) {
        document.getElementById('contentModalTitle').textContent = (hrCourseContentReadOnly ? 'Nội dung: ' : 'Xây dựng nội dung: ') + (course.title || '');
    }
    
    const libPanel = document.getElementById('builderLibraryPanel');
    if (libPanel) {
        libPanel.style.display = hrCourseContentReadOnly ? 'none' : 'flex';
    }
    
    openModal('courseContentModal');
    loadCourseContent();
    if (!hrCourseContentReadOnly) {
        loadBuilderLibrary();
    }
}

async function loadBuilderLibrary() {
    try {
        if (!hrDocumentLibraryData || !hrDocumentLibraryData.modules.length) {
            await loadDocumentLibrary();
        }
        renderBuilderBoards();
    } catch (e) {
        showToast('Lỗi tải thư viện builder: ' + e.message, 'error');
    }
}

function renderBuilderBoards() {
    const levelFilter = document.getElementById('builderLevelFilter').value;
    const codeFilter = (document.getElementById('builderCourseCodeFilter')?.value || '').trim().toLowerCase();
    
    const filterFn = (item) => {
        const matchesLevel = !levelFilter || String(item.level) === String(levelFilter);
        const itemCode = (item.courseCode || item.CourseCode || '').toLowerCase();
        const matchesCode = !codeFilter || itemCode.includes(codeFilter);
        return matchesLevel && matchesCode;
    };
    
    const mods = (hrDocumentLibraryData.modules || []).filter(filterFn);
    document.getElementById('libModulesList').innerHTML = mods.map(m => `
        <div class="builder-item" draggable="true" ondragstart="handleDragStart(event, 'module', ${m.moduleId}, '${m.title.replace(/'/g, "\\'")}', ${m.level || 0})">
            <span style="color:#1d4ed8">🧩</span>
            <div style="flex:1">
                <div style="font-size:12px; font-weight:600;">${hrLibraryEscape(m.title)}</div>
                <div style="font-size:10px; color:#64748b">ID: ${m.moduleId} ${m.level ? "| L" + m.level : ""} ${m.courseCode ? "| " + m.courseCode : ""}</div>
            </div>
        </div>
    `).join('') || '<div style="text-align:center; padding:15px; color:#94a3b8; font-size:11px;">Trống</div>';

    const lessons = (hrDocumentLibraryData.lessons || []).filter(filterFn);
    document.getElementById('libLessonsList').innerHTML = lessons.map(l => `
        <div class="builder-item" draggable="true" ondragstart="handleDragStart(event, 'lesson', ${l.lessonId}, '${l.title.replace(/'/g, "\\'")}', ${l.level || 0})">
            <span style="color:#15803d">📄</span>
            <div style="flex:1">
                <div style="font-size:12px; font-weight:600;">${hrLibraryEscape(l.title)}</div>
                <div style="font-size:10px; color:#64748b">${l.contentType} | ID: ${l.lessonId} ${l.level ? "| L" + l.level : ""} ${l.courseCode ? "| " + l.courseCode : ""}</div>
            </div>
        </div>
    `).join('') || '<div style="text-align:center; padding:15px; color:#94a3b8; font-size:11px;">Trống</div>';

    const exams = (hrDocumentLibraryData.exams || []).filter(filterFn);
    document.getElementById('libExamsList').innerHTML = exams.map(e => `
        <div class="builder-item" draggable="true" ondragstart="handleDragStart(event, 'exam', ${e.examId}, '${e.examTitle.replace(/'/g, "\\'")}', ${e.level || 0})">
            <span style="color:#c2410c">❓</span>
            <div style="flex:1">
                <div style="font-size:12px; font-weight:600;">${hrLibraryEscape(e.examTitle)}</div>
                <div style="font-size:10px; color:#64748b">${e.durationMinutes}p | ID: ${e.examId} ${e.level ? "| L" + e.level : ""} ${e.courseCode ? "| " + e.courseCode : ""}</div>
            </div>
        </div>
    `).join('') || '<div style="text-align:center; padding:15px; color:#94a3b8; font-size:11px;">Trống</div>';
}

async function loadCourseContent() {
    if (!hrCurrentContentCourseId) return;
    try {
        const data = await apiFetch('/api/hr/courses/' + hrCurrentContentCourseId + '/content');
        hrCurrentCourseContentParams = data;
        renderBuilderStructure();
    } catch(e) {
        showToast('Lỗi tải nội dung khóa học: ' + e.message, 'error');
    }
}

function renderBuilderStructure() {
    const data = hrCurrentCourseContentParams;
    const structureList = document.getElementById('builderStructureList');
    const emptyMsg = document.getElementById('builderEmptyMessage');
    
    if ((!data.modules || data.modules.length === 0) && (!data.exams || data.exams.length === 0)) {
        structureList.innerHTML = '';
        emptyMsg.style.display = 'block';
        if (hrCourseContentReadOnly) {
            emptyMsg.innerHTML = `
                <div style="font-size:40px; margin-bottom:15px;">📥</div>
                <div style="font-weight:600; font-size:16px;">Khóa học này chưa có nội dung</div>
            `;
        } else {
            emptyMsg.innerHTML = `
                <div style="font-size:40px; margin-bottom:15px;">📥</div>
                <div style="font-weight:600; font-size:16px;">Khóa học này chưa có nội dung</div>
                <div style="font-size:13px;">Hãy kéo Chương, Bài học hoặc Quiz từ thư viện bên trái vào đây</div>
            `;
        }
        return;
    }
    
    emptyMsg.style.display = 'none';
    let html = '';
    
    if (data.modules && data.modules.length > 0) {
        data.modules.forEach(m => {
            html += `
            <div class="structure-module">
                <div class="structure-module-header">
                    <div style="display:flex; align-items:center; gap:10px;">
                        <span style="font-size:18px;">🧩</span>
                        <div>
                            <div style="font-weight:700; color:#0f172a; font-size:13px;">${hrLibraryEscape(m.title)}</div>
                            <div style="font-size:11px; color:#64748b;">Chương học • ${m.lessons ? m.lessons.length : 0} bài học ${m.level ? "• L" + m.level : ""}</div>
                        </div>
                    </div>
                    <div style="display:flex; gap:6px;">
                        <button class="btn btn-secondary btn-sm" style="padding:4px 8px; font-size:11px;" onclick="previewModuleLessons(${m.moduleId})">Nội dung</button>
                        ${hrCourseContentReadOnly ? '' : `
                            <button class="btn btn-secondary btn-sm" style="padding:4px 8px; border:none; background:#f1f5f9;" onclick="openEditModuleModal(${m.moduleId})">📝</button>
                            <button class="btn btn-danger btn-sm" style="padding:4px 8px; border:none; background:#fef2f2; color:#ef4444;" onclick="unlinkModule(${m.moduleId})">🗑️ Gỡ</button>
                            <button class="btn btn-primary btn-sm" style="padding:4px 8px;" onclick="openLessonModal(${m.moduleId})">➕ Bài học</button>
                        `}
                    </div>
                </div>
                <div class="structure-module-body" id="drop-module-${m.moduleId}" ondragover="handleDragOver(event)" ondragleave="handleDragLeave(event)" ondrop="handleModuleDrop(event, ${m.moduleId})">
                    ${(m.lessons && m.lessons.length > 0) ? m.lessons.map(l => `
                        <div class="structure-item">
                            <div style="display:flex; align-items:center; gap:10px;">
                                <span style="font-size:13px; color:#3b82f6;">${l.contentType === "Video" ? "▶️" : "📄"}</span>
                                <span style="font-weight:600;">${hrLibraryEscape(l.title)}</span>
                                ${l.level ? '<span class="badge-level level-' + l.level + '">L' + l.level + '</span>' : ""}
                            </div>
                            <div style="display:flex; gap:4px; align-items:center;">
                                <button class="btn btn-secondary btn-sm" style="padding:2px 6px; font-size:11px;" onclick="previewLessonContent(${l.lessonId})">Nội dung</button>
                                ${hrCourseContentReadOnly ? '' : `
                                    <button class="btn btn-sm" style="padding:2px 6px; border:none; background:transparent;" onclick="openEditLessonModal(${l.lessonId})">📝</button>
                                    <button class="btn btn-sm" style="padding:2px 6px; border:none; background:transparent; color:#ef4444;" onclick="unlinkLesson(${l.lessonId})">🗑️ Gỡ</button>
                                `}
                            </div>
                        </div>
                    `).join('') : (hrCourseContentReadOnly ? '<div style="text-align:center; padding:15px; color:#cbd5e1; font-size:11px; border:1px dashed #f1f5f9; border-radius:6px;">Chương này chưa có bài học nào.</div>' : '<div style="text-align:center; padding:15px; color:#cbd5e1; font-size:11px; border:1px dashed #f1f5f9; border-radius:6px;">Kéo bài học thả vào đây</div>')}
                </div>
            </div>`;
        });
    }
    
    if (data.exams && data.exams.length > 0) {
        data.exams.forEach(e => {
            html += `
            <div class="structure-module" style="border-left:4px solid #f97316;">
                <div class="structure-module-header" style="background:#fff7ed;">
                    <div style="display:flex; align-items:center; gap:10px;">
                        <span style="font-size:18px;">❓</span>
                        <div>
                            <div style="font-weight:700; color:#c2410c; font-size:13px;">${hrLibraryEscape(e.examTitle)}</div>
                            <div style="font-size:11px; color:#9a3412;">Quiz • ${e.durationMinutes}p • Đỗ ${e.passScore} ${e.level ? "• L" + e.level : ""}</div>
                        </div>
                    </div>
                    ${hrCourseContentReadOnly ? '' : `
                    <div style="display:flex; gap:6px;">
                        <button class="btn btn-secondary btn-sm" style="padding:4px 8px; border:none; background:#fff;" onclick="openEditExamModal(${e.examId})">📝</button>
                        <button class="btn btn-danger btn-sm" style="padding:4px 8px; border:none; background:#fff; color:#ef4444;" onclick="unlinkExam(${e.examId})">🗑️ Gỡ</button>
                    </div>
                    `}
                </div>
            </div>`;
        });
    }
    structureList.innerHTML = html;
}

// Drag & Drop event handlers
function handleDragStart(e, type, id, title, level) {
    hrDragData = { type, id, title, level };
    e.dataTransfer.setData('text/plain', id);
}

document.addEventListener('dragend', (e) => {
    if (e.target && e.target.classList) e.target.classList.remove('dragging');
    document.querySelectorAll('.drop-zone-active').forEach(z => z.classList.remove('drop-zone-active'));
});

function handleDragOver(e) {
    if (hrCourseContentReadOnly) return;
    e.preventDefault();
    if(e.currentTarget) e.currentTarget.classList.add('drop-zone-active');
}

function handleDragLeave(e) {
    if (e.currentTarget) e.currentTarget.classList.remove('drop-zone-active');
}

async function handleMainDrop(e) {
    if (hrCourseContentReadOnly) return;
    e.preventDefault();
    handleDragLeave(e);
    if (!hrDragData) return;
    if (hrDragData.type === 'module') {
        if (confirm('Gán chương "' + hrDragData.title + '" vào khóa học này?')) {
            try {
                await apiFetch(`/api/hr/modules/${hrDragData.id}/link-to-course/${hrCurrentContentCourseId}`, {
                    method: 'POST'
                });
                showToast('Đã gán chương vào khóa học.'); 
                loadCourseContent();
                await loadDocumentLibrary();
            } catch (err) { showToast(err.message, 'error'); }
        }
    } else if (hrDragData.type === 'exam') {
        if (confirm('Gán Quiz "' + hrDragData.title + '" vào khóa học này?')) {
            try {
                const result = await apiFetch(`/api/hr/exams/${hrDragData.id}/link-to-course/${hrCurrentContentCourseId}`, {
                    method: 'POST'
                });
                if (result.info) {
                    showToast(result.info, 'warning');
                } else {
                    showToast('Đã gán Quiz vào khóa học.'); 
                }
                loadCourseContent();
                await loadDocumentLibrary();
            } catch (err) { 
                showToast(err.message, 'error'); 
            }
        }
    } else if (hrDragData.type === 'lesson') {
        showToast('Kéo bài học thả vào một chương cụ thể!', 'warning');
    }
    hrDragData = null;
}

async function handleModuleDrop(e, moduleId) {
    if (hrCourseContentReadOnly) return;
    e.stopPropagation(); e.preventDefault();
    handleDragLeave(e);
    if (!hrDragData || hrDragData.type !== 'lesson') return;
    if (confirm('Gán bài học "' + hrDragData.title + '" vào chương này?')) {
        try {
            await apiFetch(`/api/hr/lessons/${hrDragData.id}/link-to-module/${moduleId}`, {
                method: 'POST'
            });
            showToast('Đã gán bài học vào chương.'); 
            loadCourseContent();
            await loadDocumentLibrary();
        } catch (err) { showToast(err.message, 'error'); }
    }
    hrDragData = null;
}

// Unlinking content
async function unlinkModule(moduleId) {
    if (!confirm('Gỡ chương này khỏi khóa học? (Chương vẫn còn trong Kho tài liệu)')) return;
    try {
        await apiFetch(`/api/hr/modules/${moduleId}/unlink-from-course/${hrCurrentContentCourseId}`, { method: 'POST' });
        showToast('Đã gỡ chương khỏi khóa học.');
        loadCourseContent();
        await loadDocumentLibrary();
    } catch (e) { showToast(e.message, 'error'); }
}

async function unlinkLesson(lessonId) {
    if (!confirm('Gỡ bài học này khỏi chương?')) return;
    try {
        await apiFetch(`/api/hr/lessons/${lessonId}/unlink-from-module`, { method: 'POST' });
        showToast('Đã gỡ bài học.');
        loadCourseContent();
        await loadDocumentLibrary();
    } catch (e) { showToast(e.message, 'error'); }
}

async function unlinkExam(examId) {
    if (!confirm('Gỡ Quiz này khỏi khóa học?')) return;
    try {
        await apiFetch(`/api/hr/exams/${examId}/unlink-from-course/${hrCurrentContentCourseId}`, { method: 'POST' });
        showToast('Đã gỡ Quiz.');
        loadCourseContent();
        await loadDocumentLibrary();
    } catch (e) { showToast(e.message, 'error'); }
}

// Module CRUD inside course builder
function openModuleModal() {
    const titleEl = document.getElementById('moduleTitleInput');
    const levelEl = document.getElementById('moduleLevelInput');
    if (titleEl) titleEl.value = '';
    if (levelEl) levelEl.value = '1';
    openModal('moduleModal');
}

async function openEditModuleModal(id) {
    try {
        const modules = hrDocumentLibraryData.modules || [];
        let m = modules.find(x => x.moduleId == id);
        if (!m) {
            if (hrCurrentCourseContentParams.modules) {
                m = hrCurrentCourseContentParams.modules.find(x => x.moduleId == id);
            }
        }
        if (!m) return showToast('Không tìm thấy chương!', 'error');

        const idEl = document.getElementById('editModuleId');
        const titleEl = document.getElementById('editModuleTitleInput');
        const levelEl = document.getElementById('editModuleLevelInput');

        if (idEl) idEl.value = id;
        if (titleEl) titleEl.value = m.title || m.moduleTitle || '';
        if (levelEl) levelEl.value = m.level || '1';

        openModal('editModuleModal');
    } catch(e) {
        showToast(e.message, 'error');
    }
}

async function submitModule() {
    const title = document.getElementById('moduleTitleInput').value.trim();
    const level = parseInt(document.getElementById('moduleLevelInput').value) || 1;
    if (!title) return showToast('Nhập tiêu đề chương!', 'error');

    const body = { title, level };

    try {
        const courseId = hrCurrentContentCourseId || 0;
        await apiFetch(`/api/hr/courses/${courseId}/modules`, { method: 'POST', body: JSON.stringify(body) });
        showToast('Thêm chương thành công!');
        closeModal('moduleModal');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
    } catch(e) {
        showToast(e.message || 'Lỗi lưu chương', 'error');
    }
}

async function submitEditModule() {
    const id = document.getElementById('editModuleId').value;
    const title = document.getElementById('editModuleTitleInput').value.trim();
    const level = parseInt(document.getElementById('editModuleLevelInput').value) || null;
    if (!title) { showToast('Nhập tên chương!', 'error'); return; }
    try {
        await apiFetch(`/api/hr/modules/${id}`, { method: 'PUT', body: JSON.stringify({ title, level }) });
        closeModal('editModuleModal');
        showToast('Sửa chương thành công!');
        loadCourseContent();
        await loadDocumentLibrary();
    } catch(e) { showToast(e.message || 'Lỗi', 'error'); }
}

async function deleteModule(moduleId) {
    if (!confirm('Xóa hoàn toàn chương này khỏi hệ thống (bao gồm các bài học bên trong)?')) return;
    try {
        await apiFetch(`/api/hr/modules/${moduleId}`, { method: 'DELETE' });
        showToast('Xóa chương thành công.');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
        renderDocumentLibrary();
    } catch(e) {
        showToast(e.message, 'error');
    }
}

// Lesson CRUD inside course builder
async function openLessonModal(moduleId) {
    document.getElementById('lessonModuleId').value = moduleId;
    document.getElementById('lessonTitleInput').value = '';
    document.getElementById('lessonLevelInput').value = '1';
    
    // Clear video file inputs
    const vFile = document.getElementById('lessonVideoFileInput');
    const vLink = document.getElementById('lessonVideoLinkInput');
    const dFile = document.getElementById('lessonDocFileInput');
    const dLink = document.getElementById('lessonExternalDocLinkInput');
    const promptInput = document.getElementById('aiLessonPrompt');
    const statusDiv = document.getElementById('aiLessonStatus');
    const bodyInput = document.getElementById('lessonBodyInput');
    
    if (vFile) vFile.value = '';
    if (vLink) vLink.value = '';
    if (dFile) dFile.value = '';
    if (dLink) dLink.value = '';
    if (promptInput) promptInput.value = '';
    if (bodyInput) bodyInput.value = '';
    if (statusDiv) statusDiv.innerHTML = '';
    
    switchLessonSource('video', 'lesson');
    openModal('lessonModal');
}

async function openEditLessonModal(id) {
    try {
        const lessons = hrDocumentLibraryData.lessons || [];
        let l = lessons.find(x => x.lessonId == id);
        if (!l) {
            // Find in current course content modules
            if (hrCurrentCourseContentParams.modules) {
                for (const m of hrCurrentCourseContentParams.modules) {
                    if (m.lessons) {
                        const found = m.lessons.find(x => x.lessonId == id);
                        if (found) { l = found; break; }
                    }
                }
            }
        }
        
        if (!l) return showToast('Không tìm thấy bài học!', 'error');

        document.getElementById('editLessonId').value = id;
        document.getElementById('editLessonTitleInput').value = l.title || '';
        document.getElementById('editLessonLevelInput').value = l.level || '1';

        // Set video link or body
        const vLink = document.getElementById('editLessonVideoLinkInput');
        const vFile = document.getElementById('editLessonVideoFileInput');
        const bodyInput = document.getElementById('editLessonBodyInput');
        const dFile = document.getElementById('editLessonDocFileInput');
        const dLink = document.getElementById('editLessonExternalDocLinkInput');
        
        if (vLink) vLink.value = l.videoUrl || '';
        if (vFile) vFile.value = '';
        if (bodyInput) bodyInput.value = l.contentBody || '';
        if (dFile) dFile.value = '';
        if (dLink) dLink.value = '';
        
        // Show file display info if video/text
        const currentVideoStatus = document.getElementById('editLessonCurrentVideoStatus');
        if (currentVideoStatus) {
            if (l.videoUrl) {
                currentVideoStatus.innerHTML = `<span style="font-size:12px;color:#10b981;">▶️ Đã có video: ${l.videoUrl.substring(0, 40)}... </span>
                    <button class="btn btn-sm btn-danger" type="button" style="padding:2px 6px; font-size:11px;" onclick="removeCurrentLessonVideo()">Gỡ video</button>`;
            } else {
                currentVideoStatus.innerHTML = '<span style="font-size:12px;color:#94a3b8;">Chưa tải lên video</span>';
            }
        }
        
        hrIsLessonVideoDeleted = false;

        const source = l.contentType === 'Text' ? 'ai' : 'video';
        switchLessonSource(source, 'edit');
        openModal('editLessonModal');
    } catch (e) {
        showToast(e.message, 'error');
    }
}

function removeCurrentLessonVideo() {
    hrIsLessonVideoDeleted = true;
    const vLink = document.getElementById('editLessonVideoLinkInput');
    if (vLink) vLink.value = '';
    const currentVideoStatus = document.getElementById('editLessonCurrentVideoStatus');
    if (currentVideoStatus) currentVideoStatus.innerHTML = '<span style="font-size:12px;color:#ef4444;">Đã xóa video cũ. Quý khách vui lòng tải file mới hoặc lưu.</span>';
}

async function submitLesson() {
    const moduleId = document.getElementById('lessonModuleId').value;
    const formData = new FormData();
    formData.append('title', document.getElementById('lessonTitleInput').value.trim());
    formData.append('level', document.getElementById('lessonLevelInput').value);
    
    if (!formData.get('title')) return showToast('Nhập tiêu đề bài học!', 'error');
    
    const tabs = document.querySelectorAll('.lesson-source-tab');
    let source = 'video';
    tabs.forEach(t => { if(t.classList.contains('active')) source = t.getAttribute('data-source'); });
    
    if (source === 'video') {
        formData.append('contentType', 'Video');
        const videoLink = document.getElementById('lessonVideoLinkInput').value.trim();
        const videoFile = document.getElementById('lessonVideoFileInput').files[0];
        if (videoFile) formData.append('videoFile', videoFile);
        if (videoLink) formData.append('videoUrl', videoLink);
    } else if (source === 'ai') {
        formData.append('contentType', 'Text');
        formData.append('contentBody', document.getElementById('lessonBodyInput').value);
    }

    try {
        const res = await apiFetch(`/api/hr/modules/${moduleId}/lessons`, { method: 'POST', body: formData, isFormData: true });
        
        // Upload additional attachments
        await uploadLessonAssets(res.id, 'lessonDocFileInput', 'lessonExternalDocLinkInput');
        
        showToast('Tạo bài học thành công!');
        closeModal('lessonModal');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
    } catch(e) {
        showToast(e.message || 'Lỗi tạo bài học', 'error');
    }
}

async function submitEditLesson() {
    const id = document.getElementById('editLessonId').value;
    const title = document.getElementById('editLessonTitleInput').value.trim();
    const level = document.getElementById('editLessonLevelInput').value;
    if (!title) return showToast('Nhập tiêu đề bài học!', 'error');

    const formData = new FormData();
    formData.append('title', title);
    formData.append('level', level);
    formData.append('isDeleteVideo', hrIsLessonVideoDeleted ? 'true' : 'false');

    const tabs = document.querySelectorAll('.edit-lesson-source-tab');
    let source = 'video';
    tabs.forEach(t => { if(t.classList.contains('active')) source = t.getAttribute('data-source'); });

    if (source === 'video') {
        formData.append('contentType', 'Video');
        const videoLink = document.getElementById('editLessonVideoLinkInput').value.trim();
        const videoFile = document.getElementById('editLessonVideoFileInput').files[0];
        if (videoFile) formData.append('videoFile', videoFile);
        if (videoLink) formData.append('videoUrl', videoLink);
    } else if (source === 'ai') {
        formData.append('contentType', 'Text');
        formData.append('contentBody', document.getElementById('editLessonBodyInput').value);
    }

    try {
        await apiFetch(`/api/hr/lessons/${id}`, { method: 'PUT', body: formData, isFormData: true });
        
        // Upload additional attachments
        await uploadLessonAssets(id, 'editLessonDocFileInput', 'editLessonExternalDocLinkInput');
        
        showToast('Cập nhật bài học thành công!');
        closeModal('editLessonModal');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
    } catch(e) {
        showToast(e.message || 'Lỗi cập nhật bài học', 'error');
    }
}

async function uploadLessonAssets(lessonId, fileInputId, linkInputId) {
    const fileEl = document.getElementById(fileInputId);
    const linkEl = document.getElementById(linkInputId);
    
    if (fileEl && fileEl.files && fileEl.files.length > 0) {
        const formData = new FormData();
        formData.append('file', fileEl.files[0]);
        await apiFetch(`/api/hr/lessons/${lessonId}/attachments/upload`, {
            method: 'POST',
            body: formData,
            isFormData: true
        });
    }
    
    if (linkEl && linkEl.value.trim()) {
        const url = linkEl.value.trim();
        await apiFetch(`/api/hr/lessons/${lessonId}/attachments/link`, {
            method: 'POST',
            body: JSON.stringify({ url: url })
        });
    }
}

async function deleteLesson(lessonId) {
    if (!confirm('Xóa hoàn toàn bài học này khỏi hệ thống?')) return;
    try {
        await apiFetch(`/api/hr/lessons/${lessonId}`, { method: 'DELETE' });
        showToast('Xóa bài học thành công.');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
        renderDocumentLibrary();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

// Exam/Quiz CRUD inside course builder
function openExamModal() {
    const titleEl = document.getElementById('examTitleInput');
    const durationEl = document.getElementById('examDurationInput');
    const passEl = document.getElementById('examPassScoreInput');
    const maxEl = document.getElementById('examMaxAttemptsInput');
    const startEl = document.getElementById('examStartDateInput');
    const endEl = document.getElementById('examEndDateInput');
    
    if (titleEl) titleEl.value = '';
    if (durationEl) durationEl.value = '30';
    if (passEl) passEl.value = '50';
    if (maxEl) maxEl.value = '';
    if (startEl) startEl.value = '';
    if (endEl) endEl.value = '';
    
    hrLastGeneratedQuestions = [];
    
    const deptInput = document.getElementById('examTargetDeptInput');
    if (deptInput) {
        const deptOpts = '<option value="">-- Tất cả phòng ban --</option>' + (hrLoadedDepartmentsList || []).map(d => `<option value="${d.departmentId}">${d.departmentName}</option>`).join('');
        deptInput.innerHTML = deptOpts;
    }
    
    const statusDiv = document.getElementById('aiExamStatus');
    if (statusDiv) statusDiv.textContent = '';
    
    if (typeof populateLessonsDropdown === 'function') {
        populateLessonsDropdown('examLessonAI');
    }
    
    openModal('examModal');
}

async function openEditExamModal(id) {
    try {
        const exams = hrDocumentLibraryData.exams || [];
        let e = exams.find(x => x.examId == id);
        if (!e && hrCurrentCourseContentParams.exams) {
            e = hrCurrentCourseContentParams.exams.find(x => x.examId == id);
        }
        if (!e) return showToast('Không tìm thấy bài thi!', 'error');

        document.getElementById('editExamId').value = id;
        document.getElementById('editExamTitleInput').value = e.examTitle || '';
        document.getElementById('editExamLevelInput').value = e.level || '1';
        document.getElementById('editExamDurationInput').value = e.durationMinutes || 30;
        document.getElementById('editExamPassScoreInput').value = e.passScore || 50;
        document.getElementById('editExamMaxAttemptsInput').value = e.maxAttempts || '';
        
        const formatDateTimeLocal = (dateStr) => {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const pad = (n) => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
        };
        
        document.getElementById('editExamStartDateInput').value = formatDateTimeLocal(e.startDate);
        document.getElementById('editExamStartDateInput').dataset.original = e.startDate || '';
        document.getElementById('editExamEndDateInput').value = formatDateTimeLocal(e.endDate);
        document.getElementById('editExamEndDateInput').dataset.original = e.endDate || '';
        
        const deptInput = document.getElementById('editExamTargetDeptInput');
        if (deptInput) {
            const deptOpts = '<option value="">-- Tất cả phòng ban --</option>' + (hrLoadedDepartmentsList || []).map(d => `<option value="${d.departmentId}">${d.departmentName}</option>`).join('');
            deptInput.innerHTML = deptOpts;
            deptInput.value = e.targetDepartmentId || '';
        }

        openModal('editExamModal');
    } catch(err) {
        showToast(err.message, 'error');
    }
}

async function submitExam() {
    const courseId = hrCurrentContentCourseId || 0;
    const payload = {
        examTitle: document.getElementById('examTitleInput').value.trim(),
        level: document.getElementById('examLevelInput').value ? parseInt(document.getElementById('examLevelInput').value) : null,
        durationMinutes: parseInt(document.getElementById('examDurationInput').value) || 30,
        passScore: parseFloat(document.getElementById('examPassScoreInput').value) || 50,
        maxAttempts: document.getElementById('examMaxAttemptsInput').value ? parseInt(document.getElementById('examMaxAttemptsInput').value) : null,
        startDate: document.getElementById('examStartDateInput').value || null,
        endDate: document.getElementById('examEndDateInput').value || null,
        targetDepartmentId: document.getElementById('examTargetDeptInput').value ? parseInt(document.getElementById('examTargetDeptInput').value) : null,
        aiQuestions: hrLastGeneratedQuestions || []
    };

    if (!payload.examTitle) return showToast('Nhập tiêu đề quiz!', 'error');
    if (payload.passScore < 0) return showToast('Điểm của quiz không được âm!', 'error');
    if (payload.maxAttempts !== null && payload.maxAttempts < 0) return showToast('Số lần làm bài không được âm!', 'error');
    if (payload.durationMinutes < 0) return showToast('Thời gian làm bài không được âm!', 'error');
    
    const now = new Date();
    const minTime = new Date(now.getTime() - 5 * 60 * 1000);
    if (payload.startDate) {
        if (new Date(payload.startDate) < minTime) {
            return showToast('Ngày bắt đầu không được ở trong quá khứ!', 'error');
        }
    }
    if (payload.endDate) {
        if (new Date(payload.endDate) < minTime) {
            return showToast('Ngày kết thúc không được ở trong quá khứ!', 'error');
        }
    }
    if (payload.startDate && payload.endDate) {
        if (new Date(payload.endDate) < new Date(payload.startDate)) {
            return showToast('Ngày kết thúc không được trước ngày bắt đầu!', 'error');
        }
    }

    try {
        const result = await apiFetch(`/api/hr/courses/${courseId}/exams`, {
            method: 'POST',
            body: JSON.stringify(payload)
        });
        
        if (result.info) {
            showToast(result.info, 'warning');
        } else {
            showToast('Tạo Quiz thành công!');
        }
        
        closeModal('examModal');
        hrLastGeneratedQuestions = [];
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
        if (typeof loadExamsPageList === 'function') loadExamsPageList();
    } catch (e) {
        showToast(e.message || 'Lỗi tạo Quiz', 'error');
    }
}

async function submitEditExam() {
    const id = document.getElementById('editExamId').value;
    const payload = {
        examTitle: document.getElementById('editExamTitleInput').value.trim(),
        level: document.getElementById('editExamLevelInput').value ? parseInt(document.getElementById('editExamLevelInput').value) : null,
        durationMinutes: parseInt(document.getElementById('editExamDurationInput').value) || 30,
        passScore: parseFloat(document.getElementById('editExamPassScoreInput').value) || 50,
        maxAttempts: document.getElementById('editExamMaxAttemptsInput').value ? parseInt(document.getElementById('editExamMaxAttemptsInput').value) : null,
        startDate: document.getElementById('editExamStartDateInput').value || null,
        endDate: document.getElementById('editExamEndDateInput').value || null,
        targetDepartmentId: document.getElementById('editExamTargetDeptInput').value ? parseInt(document.getElementById('editExamTargetDeptInput').value) : null
    };

    if (!payload.examTitle) return showToast('Nhập tiêu đề quiz!', 'error');
    if (payload.passScore < 0) return showToast('Điểm của quiz không được âm!', 'error');
    if (payload.maxAttempts !== null && payload.maxAttempts < 0) return showToast('Số lần làm bài không được âm!', 'error');
    if (payload.durationMinutes < 0) return showToast('Thời gian làm bài không được âm!', 'error');

    const now = new Date();
    const minTime = new Date(now.getTime() - 5 * 60 * 1000);
    const originalStart = document.getElementById('editExamStartDateInput').dataset.original || '';
    const startChanged = payload.startDate && (!originalStart || new Date(payload.startDate).getTime() !== new Date(originalStart).getTime());
    if (payload.startDate && startChanged) {
        if (new Date(payload.startDate) < minTime) {
            return showToast('Ngày bắt đầu không được ở trong quá khứ!', 'error');
        }
    }

    const originalEnd = document.getElementById('editExamEndDateInput').dataset.original || '';
    const endChanged = payload.endDate && (!originalEnd || new Date(payload.endDate).getTime() !== new Date(originalEnd).getTime());
    if (payload.endDate && endChanged) {
        if (new Date(payload.endDate) < minTime) {
            return showToast('Ngày kết thúc không được ở trong quá khứ!', 'error');
        }
    }

    if (payload.startDate && payload.endDate) {
        if (new Date(payload.endDate) < new Date(payload.startDate)) {
            return showToast('Ngày kết thúc không được trước ngày bắt đầu!', 'error');
        }
    }

    try {
        await apiFetch(`/api/hr/exams/${id}`, {
            method: 'PUT',
            body: JSON.stringify(payload)
        });
        showToast('Cập nhật Quiz thành công!');
        closeModal('editExamModal');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
        if (typeof loadExamsPageList === 'function') loadExamsPageList();
    } catch (e) {
        showToast(e.message || 'Lỗi cập nhật Quiz', 'error');
    }
}

async function deleteExam(examId) {
    if (!confirm('Xóa hoàn toàn Quiz này khỏi hệ thống?')) return;
    try {
        await apiFetch(`/api/hr/exams/${examId}`, { method: 'DELETE' });
        showToast('Xóa Quiz thành công.');
        if (hrCurrentContentCourseId) loadCourseContent();
        await loadDocumentLibrary();
        if (typeof loadExamsPageList === 'function') loadExamsPageList();
    } catch (e) {
        showToast(e.message, 'error');
    }
}


// ============================================================
// 3. EXAM BUILDER (DRAG & DROP QUESTIONS TO EXAM STRUCTURE)
// ============================================================
async function openExamBuilder(examId) {
    hrBuilderExamId = examId;
    document.getElementById('builderExamId').value = examId;
    
    const exam = hrLoadedExamsList.find(e => e.examId === examId);
    document.getElementById('examBuilderTitle').textContent = `Xây dựng cấu trúc bài thi: ${exam ? exam.examTitle : ''}`;

    if (!hrAllQuestionPoolData.length) {
        try {
            hrAllQuestionPoolData = await apiFetch(`/api/hr/questions-pool`);
        } catch (e) {
            showToast('Không tải được ngân hàng câu hỏi: ' + e.message, 'error');
        }
    }

    try {
        const questions = await apiFetch(`/api/hr/exams/${examId}/questions`);
        hrBuilderActiveExamQuestions = questions.map(q => ({
            questionId: q.questionId,
            questionText: q.questionText,
            questionType: q.questionType || 'MultipleChoice',
            points: q.points || 10
        }));
    } catch (e) {
        showToast('Không tải được câu hỏi của bài thi: ' + e.message, 'error');
        hrBuilderActiveExamQuestions = [];
    }

    renderExamBuilderPool();
    renderExamBuilderStructure();
    openModal('examBuilderModal');
}

function renderExamBuilderPool() {
    const keyword = (document.getElementById('examBuilderPoolSearch')?.value || '').trim().toLowerCase();
    
    const mcContainer = document.getElementById('builderPoolMCList');
    const essayContainer = document.getElementById('builderPoolEssayList');
    const fitbContainer = document.getElementById('builderPoolFITBList');

    if (!mcContainer || !essayContainer || !fitbContainer) return;

    let pool = hrAllQuestionPoolData || [];
    if (keyword) {
        pool = pool.filter(q => String(q.questionText || '').toLowerCase().includes(keyword));
    }

    const existingIds = new Set(hrBuilderActiveExamQuestions.map(q => q.questionId));
    pool = pool.filter(q => !existingIds.has(q.questionId));

    const renderItem = (q) => `
        <div class="builder-item" draggable="true" ondragstart="handleExamDragStart(event, ${q.questionId}, '${q.questionText.replace(/'/g, "\\'")}', '${q.questionType}')" style="display:flex; justify-content:space-between; align-items:center; padding:8px 12px; background:#fff; border:1px solid #e2e8f0; border-radius:6px; margin-bottom:6px; cursor:grab;">
            <div style="flex:1; font-size:12px; font-weight:600; color:#334155; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;">
                ${hrLibraryEscape(q.questionText)}
            </div>
            <button class="btn btn-secondary btn-sm" style="padding:2px 6px; font-size:11px; margin-left:8px;" onclick="addQuestionToExamStructure(${q.questionId})">➕</button>
        </div>
    `;

    const mcQuestions = pool.filter(q => q.questionType === 'MultipleChoice');
    const essayQuestions = pool.filter(q => q.questionType === 'Essay');
    const fitbQuestions = pool.filter(q => q.questionType === 'FillInTheBlank');

    mcContainer.innerHTML = mcQuestions.length ? mcQuestions.map(renderItem).join('') : '<div style="font-size:11px; color:#94a3b8; text-align:center; padding:12px;">Trống</div>';
    essayContainer.innerHTML = essayQuestions.length ? essayQuestions.map(renderItem).join('') : '<div style="font-size:11px; color:#94a3b8; text-align:center; padding:12px;">Trống</div>';
    fitbContainer.innerHTML = fitbQuestions.length ? fitbQuestions.map(renderItem).join('') : '<div style="font-size:11px; color:#94a3b8; text-align:center; padding:12px;">Trống</div>';
}

function renderExamBuilderStructure() {
    const list = document.getElementById('examBuilderStructureList');
    const emptyMsg = document.getElementById('examBuilderEmptyMessage');
    if (!list || !emptyMsg) return;

    if (!hrBuilderActiveExamQuestions.length) {
        list.innerHTML = '';
        emptyMsg.style.display = 'block';
        return;
    }

    emptyMsg.style.display = 'none';
    list.innerHTML = hrBuilderActiveExamQuestions.map((q, idx) => {
        const typeLabel = q.questionType === 'MultipleChoice' ? 'Trắc nghiệm' : q.questionType === 'Essay' ? 'Tự luận' : 'Điền từ';
        const typeBadge = q.questionType === 'MultipleChoice' ? 'badge-blue' : q.questionType === 'Essay' ? 'badge-purple' : 'badge-orange';
        return `
            <div style="display:flex; justify-content:space-between; align-items:center; padding:12px 16px; background:#fff; border:1px solid #cbd5e1; border-radius:8px; box-shadow:0 1px 3px rgba(0,0,0,0.05)">
                <div style="flex:1; padding-right:16px;">
                    <div style="display:flex; align-items:center; gap:8px;">
                        <span style="font-weight:700; color:#475569; font-family:monospace; font-size:13px;">#${idx+1}</span>
                        <span class="badge ${typeBadge}" style="font-size:10px;">${typeLabel}</span>
                    </div>
                    <div style="font-weight:600; color:#1e293b; font-size:13px; margin-top:4px;">${hrLibraryEscape(q.questionText)}</div>
                </div>
                <div style="display:flex; align-items:center; gap:12px;">
                    <div style="display:flex; align-items:center; gap:6px;">
                        <span style="font-size:11px; font-weight:600; color:#64748b;">Điểm:</span>
                        <input type="number" class="form-input" style="width:60px; height:28px; padding:2px 6px; font-size:12px; text-align:center;" value="${q.points}" onchange="updateQuestionPoints(${q.questionId}, this.value)">
                    </div>
                    <button class="btn btn-danger btn-sm" style="padding:4px 8px;" onclick="removeQuestionFromExamStructure(${q.questionId})">Gỡ</button>
                </div>
            </div>
        `;
    }).join('');
}

function updateQuestionPoints(questionId, value) {
    const val = parseFloat(value) || 0;
    const q = hrBuilderActiveExamQuestions.find(x => x.questionId === questionId);
    if (q) q.points = val;
}

function removeQuestionFromExamStructure(questionId) {
    hrBuilderActiveExamQuestions = hrBuilderActiveExamQuestions.filter(q => q.questionId !== questionId);
    renderExamBuilderPool();
    renderExamBuilderStructure();
}

function addQuestionToExamStructure(questionId) {
    const qPool = (hrAllQuestionPoolData || []).find(x => x.questionId === questionId);
    if (!qPool) return;

    hrBuilderActiveExamQuestions.push({
        questionId: qPool.questionId,
        questionText: qPool.questionText,
        questionType: qPool.questionType || 'MultipleChoice',
        points: 10
    });

    renderExamBuilderPool();
    renderExamBuilderStructure();
}

// Exam drag events
function handleExamDragStart(e, id, text, type) {
    hrExamDragData = { id, text, type };
    e.dataTransfer.setData('text/plain', id);
}
function handleExamDragOver(e) {
    e.preventDefault();
    if(e.currentTarget) e.currentTarget.classList.add('drop-zone-active');
}
function handleExamDragLeave(e) {
    if (e.currentTarget) e.currentTarget.classList.remove('drop-zone-active');
}
function handleExamDrop(e) {
    e.preventDefault();
    handleExamDragLeave(e);
    if (!hrExamDragData) return;
    addQuestionToExamStructure(hrExamDragData.id);
    hrExamDragData = null;
}

async function saveExamStructureClick() {
    if (!hrBuilderExamId) return;

    try {
        const ids = hrBuilderActiveExamQuestions.map(q => q.questionId);
        await apiFetch(`/api/hr/exams/${hrBuilderExamId}/save-structure`, {
            method: 'POST',
            body: JSON.stringify(ids)
        });

        showToast('Lưu cấu trúc đề thi thành công.');
        closeModal('examBuilderModal');
        await loadDocumentLibrary();
        if (typeof loadExamsPageList === 'function') loadExamsPageList();
    } catch (e) {
        showToast('Lỗi lưu cấu trúc: ' + e.message, 'danger');
    }
}


// ============================================================
// 4. EXAM QUESTION MULTI-ROW BUILDER (BATCH SAVE)
// ============================================================
let hrCurrentExamQuestions = [];

async function openExamQuestionsManagementModal(examId) {
    document.getElementById('qMgmtExamId').value = examId;
    const container = document.getElementById('questionsListContainer');
    container.innerHTML = '<div style="text-align:center; padding:20px; color:#64748b;"><span class="spinner-small"></span> Đang tải câu hỏi...</div>';
    
    const quiz = hrDocumentLibraryData.exams.find(e => e.examId === examId);
    if (quiz) {
        document.getElementById('qMgmtTitle').textContent = `Quản lý câu hỏi: ${quiz.examTitle}`;
    }

    openModal('examQuestionsManagementModal');

    try {
        const questions = await apiFetch(`/api/hr/exams/${examId}/questions`);
        hrCurrentExamQuestions = questions || [];
        renderQuestionRows();
    } catch (e) {
        container.innerHTML = `<div style="text-align:center; padding:20px; color:#ef4444;">Lỗi: ${e.message}</div>`;
    }
}

function renderQuestionRows() {
    const container = document.getElementById('questionsListContainer');
    container.innerHTML = '';
    
    if (hrCurrentExamQuestions.length === 0) {
        addNewQuestionUI();
        return;
    }

    hrCurrentExamQuestions.forEach((q, idx) => {
        addNewQuestionUI(q, idx + 1);
    });
    
    updateNextQuestionNum();
}

function addNewQuestionUI(data = null, index = null) {
    const container = document.getElementById('questionsListContainer');
    const idx = index || (container.querySelectorAll('.question-row-item').length + 1);
    
    const div = document.createElement('div');
    div.className = 'question-row-item';
    div.style = 'background:#fff; border:1px solid #e2e8f0; border-radius:10px; padding:18px; position:relative; box-shadow:0 2px 4px rgba(0,0,0,0.02); margin-bottom:15px;';
    
    const questionText = data ? data.questionText : '';
    const questionType = (data ? (data.questionType || data.QuestionType) : 'MultipleChoice') || 'MultipleChoice';
    const points = data ? data.points : 10;
    
    let options = [];
    if (data && data.options) {
        options = data.options;
    } else {
        if (questionType === 'MultipleChoice') {
            options = [
                { optionText: '', isCorrect: true },
                { optionText: '', isCorrect: false },
                { optionText: '', isCorrect: false },
                { optionText: '', isCorrect: false }
            ];
        } else if (questionType === 'FillInTheBlank') {
            options = [
                { optionText: '', isCorrect: true }
            ];
        } else {
            options = [];
        }
    }
    
    const uniqueId = 'q_opts_' + Math.random().toString(36).substr(2, 9);
    
    div.innerHTML = `
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:12px;">
            <div style="font-weight:bold; color:#1e293b; font-size:15px;">Câu ${idx}</div>
            <div style="display:flex; gap:10px; align-items:center;">
                <select class="form-input q-type" style="width:140px; padding:4px 8px; font-size:12px; margin:0;" onchange="handleQuestionTypeChange(this, '${uniqueId}')">
                    <option value="MultipleChoice" ${questionType === 'MultipleChoice' ? 'selected' : ''}>Trắc nghiệm</option>
                    <option value="FillInTheBlank" ${questionType === 'FillInTheBlank' ? 'selected' : ''}>Điền chỗ trống</option>
                    <option value="Essay" ${questionType === 'Essay' ? 'selected' : ''}>Tự luận</option>
                </select>
                <button class="btn btn-danger btn-sm" onclick="this.closest('.question-row-item').remove(); updateNextQuestionNum(); redistributePoints();" style="padding:4px 8px; font-size:11px;">🗑️ Loại bỏ</button>
            </div>
        </div>
        <div class="form-group" style="margin-bottom:12px;">
            <input type="text" class="form-input q-text" placeholder="Nhập nội dung câu hỏi..." value="${hrLibraryEscape(questionText)}">
        </div>
        <div id="${uniqueId}" class="options-container">
            <!-- options will be rendered here -->
        </div>
        <div style="margin-top:10px; display:flex; justify-content:flex-end;">
            <div style="display:flex; align-items:center; gap:6px;">
                <label style="font-size:12px; color:#64748b;">Điểm:</label>
                <input type="number" class="form-input q-points" style="width:60px; padding:4px; margin:0;" value="${points}">
            </div>
        </div>
    `;
    
    container.appendChild(div);
    renderQuestionOptionsUI(div.querySelector(`#${uniqueId}`), questionType, options);
    updateNextQuestionNum();
    redistributePoints();
}

function renderQuestionOptionsUI(container, type, options = []) {
    if (type === 'MultipleChoice') {
        while (options.length < 4) {
            options.push({ optionText: '', isCorrect: options.length === 0 });
        }
        options = options.slice(0, 4);
        
        container.innerHTML = `
            <div class="grid-2" style="gap:10px; display:grid; grid-template-columns: 1fr 1fr;">
                ${options.map((opt, i) => `
                    <div style="display:flex; align-items:center; gap:8px; background:#f8fafc; padding:8px; border-radius:6px; border:1px solid #f1f5f9;">
                        <input type="checkbox" class="q-correct" value="${i}" ${opt.isCorrect ? 'checked' : ''} style="cursor:pointer; width:16px; height:16px;" onchange="handleMultipleChoiceCorrectSelect(this)">
                        <input type="text" class="form-input q-opt" style="border:none; background:transparent; padding:4px; margin:0;" placeholder="Đáp án ${i+1}" value="${hrLibraryEscape(opt.optionText)}">
                    </div>
                `).join('')}
            </div>
        `;
    } else if (type === 'FillInTheBlank') {
        const val = options.length > 0 ? options[0].optionText : '';
        container.innerHTML = `
            <div style="background:#f8fafc; padding:12px; border-radius:6px; border:1px solid #f1f5f9;">
                <label style="font-size:12px; font-weight:bold; color:#475569; display:block; margin-bottom:6px;">Đáp án chính xác cần điền:</label>
                <input type="text" class="form-input q-opt" style="margin:0; width:100%;" placeholder="Nhập đáp án đúng..." value="${hrLibraryEscape(val)}">
                <input type="checkbox" class="q-correct" checked style="display:none;" value="0">
            </div>
        `;
    } else if (type === 'Essay') {
        container.innerHTML = `
            <div style="background:#f8fafc; padding:12px; border-radius:6px; border:1px solid #f1f5f9; color:#64748b; font-size:13px; font-style:italic;">
                📝 Học viên sẽ trả lời bằng bài viết tự luận. Giảng viên sẽ chấm điểm thủ công sau khi thi xong.
            </div>
        `;
    }
}

function handleQuestionTypeChange(selectEl, containerId) {
    const type = selectEl.value;
    const container = document.getElementById(containerId);
    if (!container) return;
    
    let options = [];
    if (type === 'MultipleChoice') {
        options = [
            { optionText: '', isCorrect: true },
            { optionText: '', isCorrect: false },
            { optionText: '', isCorrect: false },
            { optionText: '', isCorrect: false }
        ];
    } else if (type === 'FillInTheBlank') {
        options = [
            { optionText: '', isCorrect: true }
        ];
    } else {
        options = [];
    }
    
    renderQuestionOptionsUI(container, type, options);
}

function handleMultipleChoiceCorrectSelect(checkbox) {
    // Cho phép chọn nhiều đáp án đúng tự do
}

function redistributePoints() {
    const examId = parseInt(document.getElementById('qMgmtExamId').value);
    const quiz = hrDocumentLibraryData.exams.find(e => e.examId === examId);
    if (!quiz) return;
    
    const rows = document.querySelectorAll('.question-row-item');
    if (rows.length === 0) return;
    
    const totalTarget = quiz.passScore || 50;
    const avg = Math.floor(totalTarget / rows.length);
    const remainder = totalTarget % rows.length;
    
    rows.forEach((row, i) => {
        const input = row.querySelector('.q-points');
        input.value = (i === rows.length - 1) ? (avg + remainder) : avg;
    });
}

function updateNextQuestionNum() {
    const container = document.getElementById('questionsListContainer');
    const count = container.querySelectorAll('.question-row-item').length;
    const nextNumEl = document.getElementById('nextQuestionNum');
    if (nextNumEl) nextNumEl.textContent = count + 1;
}

async function saveExamQuestionsBatch() {
    const examId = document.getElementById('qMgmtExamId').value;
    const container = document.getElementById('questionsListContainer');
    const rows = container.querySelectorAll('.question-row-item');
    const statusEl = document.getElementById('qMgmtStatus');
    
    const questions = [];
    let hasError = false;
    let totalPoints = 0;

    rows.forEach((row, idx) => {
        const text = row.querySelector('.q-text').value.trim();
        const points = parseFloat(row.querySelector('.q-points').value) || 0;
        totalPoints += points;
        const qType = row.querySelector('.q-type').value;
        
        const optsEls = row.querySelectorAll('.q-opt');
        const correctCheckboxes = row.querySelectorAll('.q-correct');
        
        const options = [];
        optsEls.forEach((optEl, i) => {
            const optText = optEl.value.trim();
            if (optText || qType === 'FillInTheBlank') {
                const isCorrect = correctCheckboxes[i]?.checked || false;
                options.push({ optionText: optText, isCorrect: isCorrect });
            }
        });

        if (!text) {
            showToast(`Câu ${idx+1} chưa có nội dung!`, 'error');
            hasError = true;
            return;
        }

        if (qType === 'MultipleChoice') {
            if (options.length < 2) {
                showToast(`Câu ${idx+1} cần ít nhất 2 đáp án!`, 'error');
                hasError = true;
                return;
            }
            if (!options.some(o => o.isCorrect)) {
                showToast(`Câu ${idx+1} cần ít nhất 1 đáp án đúng!`, 'error');
                hasError = true;
                return;
            }
        } else if (qType === 'FillInTheBlank') {
            if (options.length === 0 || !options[0].optionText) {
                showToast(`Câu ${idx+1} cần điền từ khoá đáp án chính xác!`, 'error');
                hasError = true;
                return;
            }
        }

        questions.push({ questionText: text, points, questionType: qType, options });
    });

    const quiz = hrDocumentLibraryData.exams.find(e => e.examId === parseInt(examId));
    const maxAllowed = quiz ? quiz.passScore : 100;
    if (totalPoints > maxAllowed) {
        showToast(`Tổng điểm (${totalPoints}) không được vượt quá Điểm đỗ quy định (${maxAllowed})!`, 'error');
        hasError = true;
    }

    if (hasError) return;
    if (questions.length === 0) {
        showToast('Vui lòng thêm ít nhất 1 câu hỏi!', 'warning');
        return;
    }

    try {
        statusEl.innerHTML = '<span class="spinner-small"></span> Đang lưu...';
        
        await apiFetch(`/api/hr/exams/${examId}/questions/batch`, {
            method: 'POST',
            body: JSON.stringify(questions)
        });

        showToast('Đã lưu tất cả câu hỏi thành công!', 'success');
        closeModal('examQuestionsManagementModal');
        await loadDocumentLibrary();
        renderDocumentLibrary();
    } catch (e) {
        showToast('Lỗi khi lưu câu hỏi: ' + e.message, 'error');
        statusEl.innerHTML = '<span style="color:#ef4444">Lỗi lưu dữ liệu.</span>';
    } finally {
        statusEl.innerHTML = '';
    }
}

async function suggestMultipleQuestionsAI(examIdOverride = null) {
    const examId = examIdOverride || document.getElementById('qMgmtExamId').value;
    const quiz = hrDocumentLibraryData.exams.find(e => e.examId == examId);
    if (!quiz) return;

    const promptText = prompt(`Bạn muốn AI thiết kế bộ câu hỏi về chủ đề gì?\n(Ví dụ: 5 câu hỏi trắc nghiệm về ${quiz.examTitle})`, `5 câu hỏi trắc nghiệm về ${quiz.examTitle}`);
    if (!promptText) return;

    showToast('AI đang soạn thảo bộ câu hỏi...', 'info');

    try {
        const result = await apiFetch('/api/hr/generate-quiz-ai', {
            method: 'POST',
            body: JSON.stringify({ prompt: promptText })
        });

        if (result && result.questions && result.questions.length > 0) {
            if (examIdOverride) {
                await openExamQuestionsManagementModal(examId);
            }
            
            // Append generated questions
            result.questions.forEach(q => {
                addNewQuestionUI({
                    questionText: q.questionText,
                    questionType: q.questionType || q.QuestionType || 'MultipleChoice',
                    points: q.points || 10,
                    options: (q.options || []).map(opt => {
                        if (typeof opt === 'string') return { optionText: opt, isCorrect: false };
                        return {
                            optionText: opt.optionText || opt.OptionText || '',
                            isCorrect: opt.isCorrect !== undefined ? opt.isCorrect : (opt.IsCorrect || false)
                        };
                    })
                });
            });
            showToast(`AI đã soạn thảo xong ${result.questions.length} câu hỏi! Hãy kiểm tra và nhấn Lưu.`, 'success');
        } else {
            showToast('AI không trả về kết quả phù hợp.', 'warning');
        }
    } catch (e) {
        showToast('Lỗi AI: ' + e.message, 'error');
    }
}

// Question CRUD inside questions tab (separate from exams)
function openCreateQuestionModal() {
    document.getElementById('questionEditModalTitle').textContent = 'Tạo câu hỏi mới';
    document.getElementById('editQuestionId').value = '';
    document.getElementById('editQuestionTypeInput').value = 'MultipleChoice';
    document.getElementById('editQuestionTextInput').value = '';
    document.getElementById('editQuestionDifficultyInput').value = 'Medium';
    
    document.getElementById('questionOptionsEditorRows').innerHTML = '';
    toggleQuestionOptionsEditor();
    
    addQuestionOptionEditorRow('', false);
    addQuestionOptionEditorRow('', false);
    
    openModal('questionPoolEditModal');
}

async function openEditQuestionModal(id) {
    document.getElementById('questionEditModalTitle').textContent = 'Chỉnh sửa câu hỏi';
    document.getElementById('editQuestionId').value = id;

    const q = (hrAllQuestionPoolData || []).find(x => x.questionId === id);
    if (!q) return;

    document.getElementById('editQuestionTypeInput').value = q.questionType || 'MultipleChoice';
    document.getElementById('editQuestionTextInput').value = q.questionText || '';
    document.getElementById('editQuestionDifficultyInput').value = q.difficulty || 'Medium';

    const optionsContainer = document.getElementById('questionOptionsEditorRows');
    optionsContainer.innerHTML = '';
    
    toggleQuestionOptionsEditor();

    if (q.options && q.options.length) {
        q.options.forEach(o => {
            addQuestionOptionEditorRow(o.optionText, o.isCorrect);
        });
    } else {
        addQuestionOptionEditorRow('', false);
        addQuestionOptionEditorRow('', false);
    }

    openModal('questionPoolEditModal');
}

function toggleQuestionOptionsEditor() {
    const type = document.getElementById('editQuestionTypeInput').value;
    const wrapper = document.getElementById('questionOptionsEditorWrapper');
    if (wrapper) wrapper.style.display = (type === 'Essay') ? 'none' : 'block';
}

function addQuestionOptionEditorRow(text = '', isCorrect = false) {
    const container = document.getElementById('questionOptionsEditorRows');
    if (!container) return;
    
    const div = document.createElement('div');
    div.style = 'display:flex; align-items:center; gap:8px; margin-bottom:8px;';
    div.innerHTML = `
        <input type="checkbox" class="opt-correct" ${isCorrect ? 'checked' : ''} style="width:16px; height:16px; cursor:pointer;">
        <input type="text" class="form-input opt-text" style="flex:1; margin:0;" placeholder="Nhập nội dung đáp án/từ khóa..." value="${hrLibraryEscape(text)}">
        <button class="btn btn-danger btn-sm" type="button" onclick="this.parentElement.remove()" style="padding:4px 8px;">🗑️</button>
    `;
    container.appendChild(div);
}

async function submitQuestionPoolForm() {
    const id = document.getElementById('editQuestionId').value;
    const questionText = document.getElementById('editQuestionTextInput').value.trim();
    const questionType = document.getElementById('editQuestionTypeInput').value;
    const difficulty = document.getElementById('editQuestionDifficultyInput').value;

    if (!questionText) return showToast('Nhập nội dung câu hỏi!', 'error');

    const options = [];
    if (questionType !== 'Essay') {
        const rows = document.querySelectorAll('#questionOptionsEditorRows > div');
        rows.forEach(row => {
            const txt = row.querySelector('.opt-text').value.trim();
            const correct = row.querySelector('.opt-correct').checked;
            if (txt) {
                options.push({ optionText: txt, isCorrect: correct });
            }
        });

        if (options.length < 1) return showToast('Cần thêm ít nhất 1 đáp án/từ khóa!', 'error');
        if (questionType === 'MultipleChoice' && !options.some(o => o.isCorrect)) {
            return showToast('Trắc nghiệm cần có ít nhất 1 đáp án đúng!', 'error');
        }
    }

    const payload = { questionText, questionType, difficulty, options };

    try {
        if (id) {
            await apiFetch(`/api/hr/questions-pool/${id}`, {
                method: 'PUT',
                body: JSON.stringify(payload)
            });
            showToast('Cập nhật câu hỏi thành công.');
        } else {
            await apiFetch(`/api/hr/questions-pool`, {
                method: 'POST',
                body: JSON.stringify(payload)
            });
            showToast('Tạo câu hỏi thành công.');
        }

        closeModal('questionPoolEditModal');
        await loadDocumentLibrary();
        renderDocumentLibrary();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function deleteQuestion(questionId) {
    if (!confirm('Xóa hoàn toàn câu hỏi này khỏi hệ thống? (Các bài thi đang chứa câu hỏi này cũng sẽ bị gỡ)')) return;
    try {
        await apiFetch(`/api/hr/questions-pool/${questionId}`, { method: 'DELETE' });
        showToast('Xóa câu hỏi thành công.');
        await loadDocumentLibrary();
        renderDocumentLibrary();
    } catch (e) {
        showToast(e.message, 'error');
    }
}


// ============================================================
// 5. PREVIEW UTILITIES
// ============================================================
async function previewModuleLessons(moduleId) {
    try {
        let m = (hrDocumentLibraryData.modules || []).find(x => x.moduleId === moduleId);
        if (!m && hrCurrentCourseContentParams && hrCurrentCourseContentParams.modules) {
            m = hrCurrentCourseContentParams.modules.find(x => x.moduleId === moduleId);
        }
        if (!m) return;

        let lessons = (hrDocumentLibraryData.lessons || []).filter(l => l.moduleId === moduleId);
        if (lessons.length === 0 && m.lessons) {
            lessons = m.lessons;
        }

        const courseTitle = m.courseTitle || 'Tài liệu hệ thống';

        hrCurrentPreviewModuleId = moduleId;
        hrCurrentPreviewCourseId = m.courseId || hrCurrentContentCourseId;

        document.getElementById('previewLibraryTitle').textContent = `Xem tài liệu: ${courseTitle}`;

        // Render tree structure in the left sidebar
        renderPreviewLibraryTree(moduleId);

        // Automatically select and preview the first lesson of this module if available
        if (lessons.length > 0) {
            selectPreviewLesson(lessons[0].lessonId);
        } else {
            document.getElementById('previewLibraryContent').innerHTML = `
                <div style="text-align: center; color: #64748b; margin-top: 100px;">
                    <div style="font-size: 48px; margin-bottom: 15px;">📝</div>
                    <h4>Chương này chưa có bài học</h4>
                    <p style="font-size: 13px;">Vui lòng thêm bài học vào chương này để xem nội dung.</p>
                </div>
            `;
        }

        openModal('previewLibraryModal');
    } catch (e) {
        showToast('Không tải được bài học: ' + e.message, 'error');
    }
}

async function previewLessonContent(lessonId) {
    try {
        let lesson = (hrDocumentLibraryData.lessons || []).find(l => l.lessonId === lessonId);
        if (!lesson && hrCurrentCourseContentParams && hrCurrentCourseContentParams.modules) {
            for (const m of hrCurrentCourseContentParams.modules) {
                if (m.lessons) {
                    const found = m.lessons.find(l => l.lessonId === lessonId);
                    if (found) {
                        lesson = found;
                        break;
                    }
                }
            }
        }
        if (!lesson) return;

        // Set preview context
        hrCurrentPreviewModuleId = lesson.moduleId;
        let module = (hrDocumentLibraryData.modules || []).find(m => m.moduleId === lesson.moduleId);
        if (!module && hrCurrentCourseContentParams && hrCurrentCourseContentParams.modules) {
            module = hrCurrentCourseContentParams.modules.find(m => m.moduleId === lesson.moduleId);
        }
        hrCurrentPreviewCourseId = module ? (module.courseId || hrCurrentContentCourseId) : null;

        const courseTitle = (module && module.courseTitle) ? module.courseTitle : 'Tài liệu hệ thống';
        document.getElementById('previewLibraryTitle').textContent = `Xem tài liệu: ${courseTitle}`;

        renderPreviewLibraryTree(lesson.moduleId, lessonId);
        selectPreviewLesson(lessonId);
        openModal('previewLibraryModal');
    } catch (e) {
        showToast('Lỗi xem bài học: ' + e.message, 'error');
    }
}

function renderPreviewLibraryTree(activeModuleId, selectedLessonId = null) {
    const sidebar = document.getElementById('previewLibrarySidebar');
    if (!sidebar) return;

    let targetModules = [];
    if (hrCurrentPreviewCourseId) {
        // If this module belongs to a course, show all modules in that course
        targetModules = (hrDocumentLibraryData.modules || []).filter(m => m.courseId === hrCurrentPreviewCourseId);
        if (targetModules.length === 0 && hrCurrentCourseContentParams && hrCurrentCourseContentParams.modules && hrCurrentContentCourseId === hrCurrentPreviewCourseId) {
            targetModules = hrCurrentCourseContentParams.modules;
        }
    } else {
        // Otherwise, just show this single module
        const activeModule = (hrDocumentLibraryData.modules || []).find(m => m.moduleId === activeModuleId);
        if (activeModule) targetModules = [activeModule];
    }

    // Sort modules by sortOrder if available
    targetModules.sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0));

    sidebar.innerHTML = targetModules.map(m => {
        // Get all lessons for this module
        let mLessons = (hrDocumentLibraryData.lessons || []).filter(l => l.moduleId === m.moduleId);
        if (mLessons.length === 0 && m.lessons) {
            mLessons = m.lessons;
        }
        // Sort lessons by sortOrder if available
        mLessons.sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0));

        const isCurrentModule = m.moduleId === activeModuleId;

        const lessonsHtml = mLessons.map(l => {
            const isSelected = l.lessonId === selectedLessonId;
            const bg = isSelected ? '#3b82f6' : 'transparent';
            const color = isSelected ? '#fff' : '#334155';
            const hoverStyle = isSelected ? '' : 'onmouseover="this.style.background=\'#e2e8f0\'" onmouseout="this.style.background=\'transparent\'"';

            return `
                <div class="preview-tree-lesson" 
                     onclick="selectPreviewLesson(${l.lessonId})" 
                     style="padding: 6px 10px; border-radius: 6px; font-size: 12.5px; cursor: pointer; transition: all 0.2s; background: ${bg}; color: ${color}; display: flex; align-items: center; gap: 6px; margin-bottom: 2px;"
                     ${hoverStyle}>
                    <span>📄</span>
                    <span style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex: 1;">${hrLibraryEscape(l.title)}</span>
                </div>
            `;
        }).join('');

        return `
            <div style="margin-bottom: 15px;">
                <div style="font-weight: 700; font-size: 13.5px; color: ${isCurrentModule ? '#1e3a8a' : '#475569'}; display: flex; align-items: center; gap: 6px; padding: 4px 0 6px 0; border-bottom: 1px solid ${isCurrentModule ? '#bfdbfe' : '#f1f5f9'}; margin-bottom: 6px;">
                    <span>📂</span>
                    <span>${hrLibraryEscape(m.title)}</span>
                </div>
                <div style="margin-left: 10px; display: flex; flex-direction: column; gap: 2px;">
                    ${lessonsHtml || '<div style="font-size: 11px; color: #94a3b8; padding: 4px 10px;">(Chưa có bài học)</div>'}
                </div>
            </div>
        `;
    }).join('');
}

function selectPreviewLesson(lessonId) {
    // Highlight active lesson in sidebar tree
    const items = document.querySelectorAll('.preview-tree-lesson');
    items.forEach(el => {
        const onClickStr = el.getAttribute('onclick') || '';
        if (!onClickStr.includes(`(${lessonId})`)) {
            el.style.background = 'transparent';
            el.style.color = '#334155';
            el.setAttribute('onmouseover', "this.style.background='#e2e8f0'");
            el.setAttribute('onmouseout', "this.style.background='transparent'");
        } else {
            el.style.background = '#3b82f6';
            el.style.color = '#fff';
            el.removeAttribute('onmouseover');
            el.removeAttribute('onmouseout');
        }
    });

    // Populate content area on the right
    let lesson = (hrDocumentLibraryData.lessons || []).find(l => l.lessonId === lessonId);
    if (!lesson && hrCurrentCourseContentParams && hrCurrentCourseContentParams.modules) {
        for (const m of hrCurrentCourseContentParams.modules) {
            if (m.lessons) {
                const found = m.lessons.find(l => l.lessonId === lessonId);
                if (found) {
                    lesson = found;
                    break;
                }
            }
        }
    }
    const container = document.getElementById('previewLibraryContent');
    if (!container || !lesson) return;

    let contentHtml = `
        <div style="margin-bottom: 20px; border-bottom: 1px solid #e2e8f0; padding-bottom: 15px;">
            <h2 style="font-size: 20px; font-weight: 800; color: #0f172a; margin-bottom: 6px;">${hrLibraryEscape(lesson.title)}</h2>
            <div style="display:flex; gap:6px; flex-wrap:wrap; font-size: 12px;">
                ${lesson.level ? `<span class="badge badge-info">Level ${lesson.level}</span>` : ''}
                ${lesson.contentType ? `<span class="badge badge-purple">${hrLibraryEscape(lesson.contentType)}</span>` : ''}
            </div>
        </div>
    `;

    // Video preview
    if (lesson.videoUrl) {
        contentHtml += `
            <div style="margin-bottom: 25px;">
                <label style="font-weight: 700; display: block; margin-bottom: 10px; color: #475569; font-size: 14px;">🎥 Video bài học:</label>
                <video src="${lesson.videoUrl}" controls style="width: 100%; max-height: 420px; border-radius: 10px; background: #000; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1);"></video>
            </div>
        `;
    }

    // Text content preview - Renders raw HTML using class "lesson-content-rich"
    if (lesson.contentBody) {
        contentHtml += `
            <div style="margin-bottom: 25px; padding: 20px; background: #f8fafc; border-radius: 10px; border: 1px solid #e2e8f0; box-shadow: inset 0 1px 2px rgba(0,0,0,0.02);">
                <label style="font-weight: 700; display: block; margin-bottom: 12px; color: #475569; font-size: 14px; border-bottom: 1px solid #e2e8f0; padding-bottom: 6px;">📝 Nội dung bài học:</label>
                <div class="lesson-content-rich" style="font-size: 14px; color: #334155; line-height: 1.7;">${lesson.contentBody}</div>
            </div>
        `;
    } else if (!lesson.videoUrl) {
        contentHtml += `
            <div style="margin-bottom: 25px; padding: 25px; background: #f8fafc; border-radius: 10px; border: 1px solid #e2e8f0; text-align: center; color: #64748b;">
                Bài học này chưa có nội dung chi tiết.
            </div>
        `;
    }

    // Attachments
    const attachments = lesson.attachments || lesson.lessonAttachments || [];
    if (attachments && attachments.length > 0) {
        contentHtml += `
            <div style="margin-bottom: 15px;">
                <label style="font-weight: 700; display: block; margin-bottom: 10px; color: #475569; font-size: 14px;">📁 Tài liệu đính kèm:</label>
                <div style="display: flex; flex-direction: column; gap: 8px;">
                    ${attachments.map(a => `
                        <div style="display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; background: #f1f5f9; border-radius: 8px; border: 1px solid #e2e8f0;">
                            <span style="font-size: 13px; color: #334155; display: flex; align-items: center; gap: 8px;">📄 ${hrLibraryEscape(a.fileName || a.FileName || 'Link tài liệu')}</span>
                            <a href="${a.filePath || a.FilePath}" target="_blank" class="btn btn-secondary btn-sm" style="text-decoration: none; padding: 5px 10px; font-size: 12px; font-weight: 600;">Tải về</a>
                        </div>
                    `).join('')}
                </div>
            </div>
        `;
    }

    container.innerHTML = contentHtml;
}

async function deleteAttachment(attachmentId, lessonId) {
    if (!confirm('Xóa tài liệu đính kèm này?')) return;
    try {
        await apiFetch(`/api/hr/attachments/${attachmentId}`, { method: 'DELETE' });
        showToast('Đã xóa tài liệu đính kèm thành công.');
        await loadDocumentLibrary();
        previewLessonContent(lessonId);
    } catch (e) {
        showToast(e.message, 'error');
    }
}


// ============================================================
// 6. EXAM PARTICIPANTS STATS
// ============================================================
let hrParticipantsList = [];
let hrCurrentParticipantsFilterStatus = '';

async function openExamParticipants(examId, filterStatus = '') {
    const titleEl = document.getElementById('participantsModalTitle');
    const container = document.getElementById('participantsTableBody');
    if (titleEl) titleEl.textContent = 'Đang tải dữ liệu làm bài...';
    if (container) container.innerHTML = '<tr><td colspan="6" style="text-align:center; padding:20px;"><span class="spinner-small"></span> Đang tải...</td></tr>';
    
    hrCurrentParticipantsFilterStatus = filterStatus;
    const statusSelect = document.getElementById('participantsStatusFilter');
    if (statusSelect) statusSelect.value = filterStatus;
    
    const searchInput = document.getElementById('participantsSearch');
    if (searchInput) searchInput.value = '';

    openModal('examParticipantsModal');

    try {
        const data = await apiFetch(`/api/hr/exams/${examId}/participants`);
        hrParticipantsList = data || [];
        
        const exam = courses.find(c => c.examId === examId) || hrDocumentLibraryData.exams.find(e => e.examId === examId);
        if (titleEl && exam) {
            titleEl.textContent = `Kết quả bài thi: ${exam.examTitle || exam.title}`;
        }
        
        renderParticipantsList();
    } catch (e) {
        showToast('Không tải được danh sách điểm thi: ' + e.message, 'error');
        if (container) container.innerHTML = '<tr><td colspan="6" style="text-align:center; color:#ef4444; padding:20px;">Lỗi tải dữ liệu.</td></tr>';
    }
}

function renderParticipantsList() {
    const tbody = document.getElementById('participantsTableBody');
    if (!tbody) return;

    const keyword = (document.getElementById('participantsSearch')?.value || '').trim().toLowerCase();
    const statusFilter = document.getElementById('participantsStatusFilter')?.value || hrCurrentParticipantsFilterStatus;

    let filtered = hrParticipantsList;

    if (keyword) {
        filtered = filtered.filter(p => p.fullName.toLowerCase().includes(keyword) || (p.employeeCode || '').toLowerCase().includes(keyword));
    }

    if (statusFilter) {
        filtered = filtered.filter(p => p.statusText === statusFilter);
    }

    tbody.innerHTML = filtered.length ? filtered.map(p => `
        <tr>
            <td style="font-family:monospace">${p.employeeCode || 'N/A'}</td>
            <td><strong>${hrLibraryEscape(p.fullName)}</strong></td>
            <td>${hrLibraryEscape(p.departmentName)}</td>
            <td>${p.score !== null ? `<span style="font-weight:800; font-size:14px;">${p.score}</span>` : '<span style="color:#94a3b8">--</span>'}</td>
            <td><span class="badge badge-${p.statusClass}">${p.statusText}</span></td>
            <td>${p.endTime ? hrFmtDate(p.endTime) : '<span style="color:#94a3b8">--</span>'}</td>
        </tr>
    `).join('') : '<tr><td colspan="6" style="text-align:center; color:#94a3b8; padding:20px;">Không tìm thấy nhân sự phù hợp.</td></tr>';
}

// Attach event listeners when JS is fully loaded
window.addEventListener('DOMContentLoaded', () => {
    const builderLvl = document.getElementById('builderLevelFilter');
    if (builderLvl) builderLvl.addEventListener('change', loadBuilderLibrary);
    
    const builderCode = document.getElementById('builderCourseCodeFilter');
    if (builderCode) builderCode.addEventListener('input', loadBuilderLibrary);
});

function safeEscape(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
}

function populateLessonsDropdown(selectId) {
    const selectEl = document.getElementById(selectId);
    if (!selectEl) return;
    
    const isIT = window.location.pathname.toLowerCase().includes('/it');
    const libraryData = isIT ? (window.documentLibraryData || {}) : (window.hrDocumentLibraryData || {});
    const lessons = libraryData.lessons || [];
    
    selectEl.innerHTML = '<option value="">-- Chọn bài học từ thư viện --</option>' +
        lessons.map(l => `<option value="${l.lessonId}">${safeEscape(l.title)}</option>`).join('');
}

async function generateQuizFromLesson(source = 'exam') {
    const selectEl = document.getElementById(source === 'exam' ? 'examLessonAI' : 'libraryExamLessonAI');
    const statusDiv = document.getElementById(source === 'exam' ? 'aiExamStatus' : 'aiQuizStatus');
    const btn = document.getElementById(source === 'exam' ? 'btnGenerateExamLessonAI' : 'btnGenerateQuizLessonAI');

    if (!selectEl) return;
    const lessonId = selectEl.value;
    if (!lessonId) {
        showToast('Vui lòng chọn một bài học!', 'warning');
        return;
    }

    btn.disabled = true;
    const originalText = btn.innerText;
    btn.innerText = 'Đang xử lý...';
    statusDiv.innerText = '⏳ AI đang đọc bài học và soạn thảo câu hỏi...';
    statusDiv.style.color = '#f59e0b';

    try {
        const isIT = window.location.pathname.toLowerCase().includes('/it');
        const apiPath = isIT ? '/api/it/exams/generate-from-lesson' : '/api/hr/exams/generate-from-lesson';
        
        const result = await apiFetch(apiPath, {
            method: 'POST',
            body: JSON.stringify({ lessonId: parseInt(lessonId) })
        });

        if (result && result.examTitle) {
            document.getElementById(source === 'exam' ? 'examTitleInput' : 'libraryExamTitleInput').value = result.examTitle;
            if (result.questions) {
                hrLastGeneratedQuestions = result.questions.map(q => ({
                    questionText: q.questionText,
                    questionType: q.questionType || q.QuestionType || 'MultipleChoice',
                    points: q.points || 10,
                    options: (q.options || []).map(opt => {
                        if (typeof opt === 'string') return { optionText: opt, isCorrect: false };
                        return {
                            optionText: opt.optionText || opt.OptionText || '',
                            isCorrect: opt.isCorrect !== undefined ? opt.isCorrect : (opt.IsCorrect || false)
                        };
                    })
                }));
                statusDiv.innerText = `✅ Đã soạn ${hrLastGeneratedQuestions.length} câu hỏi thành công!`;
                statusDiv.style.color = '#10b981';
                showToast('AI đã soạn thảo xong!');
            }
        }
    } catch (e) {
        statusDiv.innerText = '❌ Lỗi: ' + e.message;
        statusDiv.style.color = '#ef4444';
        showToast('Lỗi AI: ' + e.message, 'error');
    } finally {
        btn.disabled = false;
        btn.innerText = originalText;
    }
}

// ============================================================
// DOCUMENT APPROVAL (PHÊ DUYỆT TÀI LIỆU) - PHÒNG ĐÀO TẠO
// ============================================================
let pendingApprovals = [];
let selectedApprovalId = null;
let activeNotifIdToRead = null;

async function loadApprovals() {
    try {
        pendingApprovals = await apiFetch('/api/hr/approvals');
        filterApprovals();
    } catch (e) {
        showToast('Lỗi tải danh sách phê duyệt: ' + e.message, 'error');
    }
}

function filterApprovals() {
    const searchVal = (document.getElementById('approvalSearch')?.value || '').toLowerCase();
    const statusVal = document.getElementById('approvalStatusFilter')?.value;

    let filtered = pendingApprovals.filter(a => {
        const matchSearch = a.title.toLowerCase().includes(searchVal) || (a.createdBy && a.createdBy.toString().includes(searchVal));
        const matchStatus = (statusVal === '') || a.approvalStatus === statusVal;
        return matchSearch && matchStatus;
    });

    if (statusVal === '') {
        filtered.sort((a, b) => (a.approvalStatus === 'Pending' ? -1 : 1));
    }

    renderApprovalsTable(filtered);
}

function renderApprovalsTable(data) {
    const table = document.getElementById('approvalsTable');
    if (!table) return;
    table.innerHTML = data.map(a => {
        let typeLabel, typeIcon;
        if (a.targetType === 'course') { typeLabel = 'Khóa học'; typeIcon = '📚'; }
        else if (a.examName || a.targetType === 'quiz') { typeLabel = 'Quiz'; typeIcon = '📝'; }
        else if (a.lessonName || a.targetType === 'lesson') { typeLabel = 'Bài học'; typeIcon = '🎬'; }
        else if (a.moduleName || a.targetType === 'module') { typeLabel = 'Chương'; typeIcon = '📚'; }
        else { typeLabel = 'Tài liệu'; typeIcon = '📄'; }

        let statusBadge = a.approvalStatus === 'Approved' ? 'badge-success' : (a.approvalStatus === 'Rejected' ? 'badge-danger' : 'badge-warning');
        let statusText = a.approvalStatus === 'Approved' ? 'Đã duyệt' : (a.approvalStatus === 'Rejected' ? 'Từ chối' : 'Chờ duyệt');

        let creationLabel = '';
        if (a.targetType === 'course') creationLabel = `<span class="badge" style="background:#e0f2fe;color:#0369a1;margin-top:4px;">+ Yêu cầu Khóa học mới</span>`;
        if (a.newModuleName) creationLabel += `<span class="badge" style="background:#fef3c7;color:#92400e;margin-top:4px;">+ Chương: ${libraryEscape(a.newModuleName)}</span> `;
        if (a.newLessonName) creationLabel += `<span class="badge" style="background:#f0fdf4;color:#166534;margin-top:4px;">+ Bài học: ${libraryEscape(a.newLessonName)}</span> `;
        if (a.newExamName) creationLabel += `<span class="badge" style="background:#fff7ed;color:#9a3412;margin-top:4px;">+ Quiz: ${libraryEscape(a.newExamName)}</span>`;

        return `
        <tr>
            <td>${a.id}</td>
            <td>
                <div style="display:flex;align-items:center;gap:10px;">
                    <div style="font-size:24px;">${getFileIcon(a.filePath)}</div>
                    <div>
                        <strong>${libraryEscape(a.title)}</strong><br>
                        <small style="color:#64748b">${libraryEscape(a.courseName || 'Tài liệu độc lập')}</small>
                        <div style="display:flex;flex-wrap:wrap;gap:4px;">${creationLabel}</div>
                    </div>
                </div>
            </td>
            <td><span class="badge badge-info">${typeIcon} ${typeLabel}</span></td>
            <td>${libraryEscape(a.createdBy || 'Unknown')}</td>
            <td><span class="badge ${statusBadge}">${statusText}</span></td>
            <td>
                <button class="btn btn-secondary btn-sm" onclick="openApprovalDetail(${a.id})">Xem chi tiết</button>
            </td>
        </tr>
    `}).join('') || '<tr><td colspan="6" style="text-align:center;padding:24px;color:#64748b;">Không có yêu cầu nào phù hợp.</td></tr>';
}

async function openApprovalDetail(id, notifId) {
    selectedApprovalId = id;
    activeNotifIdToRead = notifId || null;
    
    let item = pendingApprovals.find(a => a.id === id);
    if (!item) {
        try {
            item = await apiFetch(`/api/hr/documents/${id}`);
        } catch (e) {
            showToast('Lỗi tải chi tiết đề xuất: ' + e.message, 'error');
            return;
        }
    }
    if (!item) return;
    
    let linkStr = 'N/A';
    if (item.examName) linkStr = 'Quiz: ' + item.examName;
    else if (item.moduleName) {
        linkStr = 'Chương: ' + item.moduleName;
        if (item.lessonName) linkStr += ' > Bài học: ' + item.lessonName;
    }

    let creationHtml = '';
    if (item.pendingData) {
        try {
            const data = JSON.parse(item.pendingData);
            if (item.targetType === 'module') {
                creationHtml = `
                    <div style="margin-top:16px; padding:16px; background:#f0fdf4; border:1px solid #dcfce7; border-radius:12px;">
                        <div style="font-weight:700; color:#166534; font-size:14px; margin-bottom:10px;">🧩 Tạo Chương học mới</div>
                        <div style="font-size:13px; color:#166534;">
                            <div><b>Tiêu đề:</b> ${libraryEscape(data.title)}</div>
                            <div><b>Level:</b> ${data.level || 1}</div>
                        </div>
                    </div>`;
            } else if (item.targetType === 'lesson') {
                creationHtml = `
                    <div style="margin-top:16px; padding:16px; background:#f0f9ff; border:1px solid #e0f2fe; border-radius:12px;">
                        <div style="font-weight:700; color:#0369a1; font-size:14px; margin-bottom:10px;">📝 Tạo Bài học mới</div>
                        <div style="font-size:13px; color:#0369a1; display:flex; flex-direction:column; gap:6px;">
                            <div><b>Tiêu đề:</b> ${libraryEscape(data.title)}</div>
                            ${data.newModuleName ? `<div style="padding:4px 8px; background:#fff; border-radius:6px; border:1px dashed #0369a1; display:inline-block; margin:4px 0;">✨ Cũng tạo chương: <b>${libraryEscape(data.newModuleName)}</b></div>` : ''}
                            <div><b>Loại:</b> ${data.contentType} | <b>Level:</b> ${data.level || 1}</div>
                            ${data.videoUrl ? `<div><b>Video:</b> <a href="${data.videoUrl}" target="_blank" style="color:#0284c7">${data.videoUrl}</a></div>` : ''}
                            ${data.contentBody ? `<div style="margin-top:8px; padding:10px; background:rgba(255,255,255,.5); border-radius:8px; font-style:italic;">"${libraryEscape(data.contentBody)}"</div>` : ''}
                        </div>
                    </div>`;
            } else if (item.targetType === 'quiz') {
                const qs = data.questions || [];
                creationHtml = `
                    <div style="margin-top:16px; padding:16px; background:#fdf2f8; border:1px solid #fce7f3; border-radius:12px;">
                        <div style="font-weight:700; color:#9d174d; font-size:14px; margin-bottom:10px;">❓ Tạo Quiz mới</div>
                        <div style="font-size:13px; color:#9d174d; margin-bottom:12px;">
                            <div><b>Tiêu đề:</b> ${libraryEscape(data.examTitle)}</div>
                            <div><b>Thời gian:</b> ${data.durationMinutes} phút | <b>Điểm đạt:</b> ${data.passScore}% | <b>Lần làm:</b> ${data.maxAttempts || 'Vô hạn'}</div>
                        </div>
                        <div style="display:flex; flex-direction:column; gap:10px;">
                            ${qs.map((q, i) => `
                                <div style="padding:10px; background:#fff; border-radius:8px; border:1px solid #fbcfe8;">
                                    <div style="font-weight:700; color:#be185d; margin-bottom:6px;">Q${i+1}: ${libraryEscape(q.questionText)}</div>
                                    <div style="display:grid; grid-template-columns:1fr 1fr; gap:6px;">
                                        ${(q.options || []).map(opt => `
                                            <div style="font-size:11px; padding:4px 8px; border-radius:4px; ${opt.isCorrect ? 'background:#dcfce7; color:#166534; border:1px solid #bbf7d0;' : 'background:#f1f5f9; color:#64748b;'}">
                                                ${opt.isCorrect ? '✓' : '○'} ${libraryEscape(opt.optionText)}
                                            </div>
                                        `).join('')}
                                    </div>
                                </div>
                            `).join('')}
                        </div>
                    </div>`;
            } else if (item.targetType === 'course') {
                creationHtml = `
                    <div style="margin-top:16px; padding:16px; background:#f0f9ff; border:1px solid #e0f2fe; border-radius:12px;">
                        <div style="font-weight:700; color:#0369a1; font-size:14px; margin-bottom:10px;">📚 Tạo Khóa học mới</div>
                        <div style="font-size:13px; color:#0369a1; display:flex; flex-direction:column; gap:6px;">
                            <div><b>Tiêu đề:</b> ${libraryEscape(data.title)}</div>
                            <div><b>Bắt buộc:</b> ${data.isMandatory ? 'Có' : 'Không'}</div>
                            <div><b>Mô tả:</b></div>
                            <div style="padding:10px; background:rgba(255,255,255,.5); border-radius:8px; font-style:italic;">"${libraryEscape(data.description || 'Không có mô tả')}"</div>
                        </div>
                    </div>`;
            }
        } catch(e) { creationHtml = `<div style="color:#ef4444; padding:10px;">Lỗi dữ liệu: ${e.message}</div>`; }
    } else if (item.newModuleName || item.newLessonName || item.newExamName) {
        creationHtml = `
            <div style="margin-top:16px; padding:12px; background:#fff7ed; border:1px solid #ffedd5; border-radius:8px;">
                <div style="font-weight:700; color:#9a3412; font-size:13px; margin-bottom:8px;">🚀 Yêu cầu tạo nội dung (Legacy):</div>
                <ul style="margin:0; padding-left:20px; font-size:13px; color:#c2410c;">
                    ${item.newModuleName ? `<li>Chương: <b>${libraryEscape(item.newModuleName)}</b></li>` : ''}
                    ${item.newLessonName ? `<li>Bài học: <b>${libraryEscape(item.newLessonName)}</b></li>` : ''}
                    ${item.newExamName ? `<li>Quiz: <b>${libraryEscape(item.newExamName)}</b></li>` : ''}
                </ul>
            </div>`;
    }

    const body = document.getElementById('approvalDetailBody');
    body.innerHTML = `
        <div style="padding:16px; background:#f8fafc; border-radius:12px; margin-bottom:16px">
            <div style="font-size:14px; color:#64748b; margin-bottom:4px">Tiêu đề tài liệu</div>
            <div style="font-size:18px; font-weight:800; color:#0f172a">${libraryEscape(item.title)}</div>
        </div>
        <div class="grid-2">
            <div class="form-group"><label>Khóa học liên kết</label><div class="form-input" style="background:#f1f5f9">${libraryEscape(item.courseName || 'Không có')}</div></div>
            <div class="form-group"><label>Gắn vào nội dung</label><div class="form-input" style="background:#f1f5f9">${libraryEscape(linkStr)}</div></div>
        </div>
        ${creationHtml}
        <div class="grid-2" style="margin-top:16px;">
            <div class="form-group"><label>Người tạo (ID)</label><div class="form-input" style="background:#f1f5f9">${item.createdBy || 'N/A'}</div></div>
            <div class="form-group"><label>Trạng thái</label><div class="form-input" style="background:#f1f5f9">${item.approvalStatus}</div></div>
        </div>
        <div class="form-group">
            <label>Đường dẫn tệp / URL</label>
            <div style="padding:16px; border:1px solid #e2e8f0; border-radius:8px; background:#fff">
                <a href="${item.filePath}" target="_blank" style="color:var(--primary); font-weight:600">${item.filePath || 'Không có đường dẫn'}</a>
            </div>
        </div>
    `;
    openModal('approvalDetailModal');
}

async function approveDocument() {
    if (!selectedApprovalId) return;
    try {
        await apiFetch(`/api/hr/approvals/${selectedApprovalId}/approve`, { method: 'POST' });
        
        if (activeNotifIdToRead) {
            await apiFetch(`/api/hr/notifications/${activeNotifIdToRead}/read`, { method: 'POST' });
            activeNotifIdToRead = null;
            if (typeof loadHrNotifications === 'function') await loadHrNotifications();
        }
        
        showToast('Đã phê duyệt tài liệu thành công.', 'success');
        closeModal('approvalDetailModal');
        loadApprovals();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function rejectDocument() {
    if (!selectedApprovalId) return;
    openModal('rejectReasonModal');
}

async function confirmReject() {
    const reason = document.getElementById('rejectReasonText').value.trim();
    if (!reason) { showToast('Vui lòng nhập lý do từ chối.', 'warning'); return; }
    
    try {
        await apiFetch(`/api/hr/approvals/${selectedApprovalId}/reject`, { 
            method: 'POST', 
            body: JSON.stringify({ reason })
        });
        
        if (activeNotifIdToRead) {
            await apiFetch(`/api/hr/notifications/${activeNotifIdToRead}/read`, { method: 'POST' });
            activeNotifIdToRead = null;
            if (typeof loadHrNotifications === 'function') await loadHrNotifications();
        }
        
        showToast('Đã từ chối yêu cầu.', 'warning');
        closeModal('rejectReasonModal');
        closeModal('approvalDetailModal');
        loadApprovals();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function approveProposalDirect(docId, notifId) {
    if (!confirm('Bạn có chắc chắn muốn phê duyệt đề xuất này?')) return;
    try {
        await apiFetch(`/api/hr/approvals/${docId}/approve`, { method: 'POST' });
        await apiFetch(`/api/hr/notifications/${notifId}/read`, { method: 'POST' });
        showToast('Đã phê duyệt tài liệu thành công.', 'success');
        if (typeof loadHrNotifications === 'function') await loadHrNotifications();
        loadApprovals();
    } catch (e) {
        showToast(e.message, 'error');
    }
}

async function rejectProposalDirect(docId, notifId) {
    selectedApprovalId = docId;
    activeNotifIdToRead = notifId;
    document.getElementById('rejectReasonText').value = '';
    openModal('rejectReasonModal');
}

async function showRejectionReason(docId) {
    try {
        const doc = await apiFetch(`/api/hr/documents/${docId}`);
        if (!doc) {
            showToast('Không tìm thấy tài liệu.', 'warning');
            return;
        }
        document.getElementById('rejectedDocTitle').textContent = doc.title || 'N/A';
        document.getElementById('rejectedDocReason').textContent = doc.rejectionReason || 'Không có lý do cụ thể từ Phòng Đào tạo.';
        openModal('rejectionReasonDetailModal');
    } catch (e) {
        showToast('Lỗi tải lý do từ chối: ' + e.message, 'error');
    }
}
