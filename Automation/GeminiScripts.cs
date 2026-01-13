namespace GeminiWebTranslator.Automation
{
    /// <summary>
    /// Gemini 웹 애플리케이션의 요소를 제어하는 JavaScript 스크립트 모음입니다.
    /// 중복을 제거하고 유지보수를 용이하게 하기 위해 통합된 선택자를 사용합니다.
    /// </summary>
    public static class GeminiScripts
    {
        #region 공통 선택자 (통합)
        
        /// <summary>
        /// 입력창 선택자 목록 (우선순위 순)
        /// </summary>
        private const string InputSelectors = @"
            document.querySelector('.ql-editor') || 
            document.querySelector('div[contenteditable=""true""]') ||
            document.querySelector('rich-textarea .ql-editor')";

        /// <summary>
        /// 응답 영역 선택자 - 단순화된 버전
        /// </summary>
        private const string ResponseSelector = "message-content.model-response-text, .model-response-text";

        #endregion

        #region 입력 관련

        /// <summary>
        /// 입력창에 포커스를 주고 기존 내용을 비웁니다.
        /// </summary>
        public const string FocusAndClearScript = @"
            (function() {
                const input = " + InputSelectors + @";
                if (!input) return false;
                input.focus();
                // TrustedHTML 정책 우회: DOM API로 내용 삭제
                document.execCommand('selectAll', false, null);
                document.execCommand('delete', false, null);
                return true;
            })();
        ";


        /// <summary>
        /// 입력창 존재 여부 확인 (빠른 체크)
        /// </summary>
        public const string CheckInputReadyScript = @"
            !!(document.querySelector('.ql-editor') || 
               document.querySelector('div[contenteditable=""true""]'))";

        /// <summary>
        /// 새 채팅 시작 - 사이드바의 새 대화 버튼 클릭 또는 URL 이동
        /// </summary>
        public const string NewChatScript = @"
            (function() {
                // 방법 1: 새 채팅 버튼 찾기 (여러 선택자 시도)
                const newChatSelectors = [
                    'button[aria-label=""새 채팅""]',
                    'button[aria-label=""New chat""]',
                    'a[href=""/app""]',
                    '[data-test-id=""new-chat-button""]',
                    '.new-chat-button',
                    'button.new-chat',
                    // 사이드바 상단의 + 버튼
                    'button[aria-label=""새 대화 시작""]',
                    'button[aria-label=""Start a new chat""]'
                ];
                
                for (const selector of newChatSelectors) {
                    const btn = document.querySelector(selector);
                    if (btn) {
                        btn.click();
                        return JSON.stringify({ success: true, method: 'button', selector: selector });
                    }
                }
                
                // 방법 2: 페이지 URL로 새 채팅 이동
                if (window.location.href.includes('/app/')) {
                    window.location.href = 'https://gemini.google.com/app';
                    return JSON.stringify({ success: true, method: 'navigation' });
                }
                
                // 방법 3: 히스토리 API 사용
                if (window.history && typeof window.history.pushState === 'function') {
                    window.history.pushState({}, '', '/app');
                    window.location.reload();
                    return JSON.stringify({ success: true, method: 'history' });
                }
                
                return JSON.stringify({ success: false, error: 'no_method_available' });
            })();
        ";

        #endregion

        #region 메시지 전송

        /// <summary>
        /// 전송 버튼 클릭
        /// </summary>
        public const string SendButtonScript = @"
            (function() {
                const btn = document.querySelector('.send-button:not(.stop)') ||
                           document.querySelector('button[aria-label=""보내기""]') ||
                           document.querySelector('button[aria-label=""Send message""]');
                if (btn && !btn.disabled) {
                    btn.click();
                    return 'clicked';
                }
                return 'not_found';
            })();
        ";

        #endregion

        #region 응답 수집

        /// <summary>
        /// 최신 응답 텍스트 추출 (이미지 응답 필터링 포함)
        /// </summary>
        public const string GetResponseScript = @"
            (function() {
                const responses = document.querySelectorAll('message-content.model-response-text, .model-response-text');
                if (responses.length === 0) return '';
                
                const lastResponse = responses[responses.length - 1];
                
                // 1. 마크다운 영역에서 텍스트 추출 시도
                const markdownEl = lastResponse.querySelector('.markdown');
                if (markdownEl) {
                    const text = markdownEl.innerText || '';
                    // 이미지 생성 관련 메타 텍스트 필터링
                    const cleaned = text.trim()
                        .replace(/^image_generated\s*/gi, '')
                        .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                        .replace(/^\[Image[^\]]*\]\s*/gi, '');
                    if (cleaned.length > 0) return cleaned;
                }
                
                // 2. 코드 블록 영역 확인
                const codeBlocks = lastResponse.querySelectorAll('code-block, pre code');
                if (codeBlocks.length > 0) {
                    let codeText = '';
                    codeBlocks.forEach(block => {
                        const code = block.innerText || block.textContent || '';
                        codeText += code + '\n';
                    });
                    if (codeText.trim().length > 0) return codeText.trim();
                }
                
                // 3. 일반 텍스트 추출 (이미지 버튼 영역 제외)
                let text = '';
                const walker = document.createTreeWalker(lastResponse, NodeFilter.SHOW_TEXT, {
                    acceptNode: function(node) {
                        // 이미지 버튼이나 이미지 관련 요소 내부 텍스트 제외
                        const parent = node.parentElement;
                        if (parent && (
                            parent.closest('button.image-button') ||
                            parent.closest('.image-container') ||
                            parent.closest('[data-image]') ||
                            parent.closest('.generated-image') ||
                            parent.tagName === 'BUTTON'
                        )) {
                            return NodeFilter.FILTER_REJECT;
                        }
                        return NodeFilter.FILTER_ACCEPT;
                    }
                });
                
                let node;
                while (node = walker.nextNode()) {
                    const nodeText = node.textContent.trim();
                    if (nodeText.length > 0) {
                        text += nodeText + ' ';
                    }
                }
                
                text = text.trim()
                    .replace(/^image_generated\s*/gi, '')
                    .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                    .replace(/^\[Image[^\]]*\]\s*/gi, '');
                
                // 4. 최종 fallback: innerText 직접 사용
                if (text.length === 0) {
                    text = (lastResponse.innerText || '').trim()
                        .replace(/^image_generated\s*/gi, '')
                        .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                        .replace(/^\[Image[^\]]*\]\s*/gi, '');
                }
                
                return text;
            })();
        ";

        /// <summary>
        /// 응답 개수 확인
        /// </summary>
        public const string GetResponseCountScript = @"
            document.querySelectorAll('message-content.model-response-text, .model-response-text').length";

        #endregion

        #region 상태 감지

        /// <summary>
        /// 생성 중인지 확인 (단순화)
        /// </summary>
        public const string IsGeneratingScript = @"
            (function() {
                // 1. Stop 버튼 확인
                const sendBtn = document.querySelector('.send-button');
                if (sendBtn && sendBtn.classList.contains('stop')) return true;
                
                // 2. aria-busy 확인
                const lastMarkdown = [...document.querySelectorAll('.markdown')].pop();
                if (lastMarkdown && lastMarkdown.getAttribute('aria-busy') === 'true') return true;
                
                // 3. 중지 버튼 노출 확인
                const stopBtn = document.querySelector('button[aria-label*=""중지""], button[aria-label*=""Stop""]');
                if (stopBtn && stopBtn.offsetParent !== null && !stopBtn.disabled) return true;
                
                return false;
            })();
        ";

        /// <summary>
        /// Gemini 응답 생성 중지 스크립트
        /// 생성 중일 때 중지 버튼을 클릭하여 응답을 멈춥니다.
        /// </summary>
        public const string StopGeminiResponseScript = @"
            (function() {
                try {
                    // 1. Send 버튼이 Stop 상태인 경우 (가장 일반적)
                    var sendBtn = document.querySelector('.send-button.stop');
                    if (sendBtn && sendBtn.offsetParent !== null && !sendBtn.disabled) {
                        sendBtn.click();
                        return 'stopped_via_send_button';
                    }
                    
                    // 2. 별도의 중지 버튼 검색
                    var stopSelectors = [
                        'button[aria-label*=""중지""]',
                        'button[aria-label*=""Stop""]',
                        'button[aria-label=""대답 생성 중지""]',
                        'button[aria-label=""Stop generating""]'
                    ];
                    
                    for (var i = 0; i < stopSelectors.length; i++) {
                        var btn = document.querySelector(stopSelectors[i]);
                        if (btn && btn.offsetParent !== null && !btn.disabled) {
                            btn.click();
                            return 'stopped_via_selector';
                        }
                    }
                    
                    // 3. mat-icon으로 stop 검색 (Material Design 아이콘)
                    var icons = document.querySelectorAll('mat-icon');
                    for (var j = 0; j < icons.length; j++) {
                        var icon = icons[j];
                        if (icon.textContent === 'stop' || icon.textContent === 'stop_circle') {
                            var parentBtn = icon.closest('button');
                            if (parentBtn && parentBtn.offsetParent !== null && !parentBtn.disabled) {
                                parentBtn.click();
                                return 'stopped_via_mat_icon';
                            }
                        }
                    }
                    
                    return 'no_stop_button_found';
                } catch (e) {
                    return 'error: ' + e.message;
                }
            })();
        ";

        /// <summary>
        /// 다음 입력 대기 상태 확인
        /// </summary>
        public const string IsReadyForNextInputScript = @"
            (function() {
                const input = document.querySelector('.ql-editor, div[contenteditable=""true""]');
                if (!input || input.getAttribute('contenteditable') !== 'true') return false;
                
                const sendBtn = document.querySelector('.send-button');
                if (sendBtn && sendBtn.classList.contains('stop')) return false;
                
                return input.textContent.trim() === '' || input.classList.contains('ql-blank');
            })();
        ";

        #endregion

        #region 로그인 상태

        /// <summary>
        /// 로그인 필요 여부 확인
        /// </summary>
        public const string LoginCheckScript = @"
            document.querySelector('button[aria-label*=""Sign in""], button[aria-label*=""로그인""]') ? 'login_needed' : 'ok'";

        /// <summary>
        /// 로그인 상태 정밀 진단
        /// </summary>
        public const string DiagnoseLoginScript = @"
            (function() {
                const loginBtn = document.querySelector('button[aria-label*=""로그인""], button[aria-label*=""Sign in""]');
                return loginBtn && loginBtn.offsetParent !== null ? 'logged_out' : 'logged_in';
            })()
        ";

        /// <summary>
        /// 오류 메시지 수집 (스낵바, 알럿 등 정밀 진단)
        /// </summary>
        public const string DiagnoseErrorScript = @"
            (function() {
                // 1. 하단 스낵바 (문제가 발생했습니다 등)
                const snackbar = document.querySelector('m-snackbar, snack-bar, .snackbar, .cdk-overlay-container .error');
                if (snackbar && snackbar.offsetParent !== null && snackbar.innerText.trim().length > 0) {
                    return snackbar.innerText.trim().substring(0, 100);
                }
                
                // 2. 알림 영역
                const alert = document.querySelector('[role=""alert""], .simple-message.error');
                if (alert && alert.offsetParent !== null && alert.innerText.trim().length > 0) {
                    return alert.innerText.trim().substring(0, 100);
                }
                
                // 3. 브라우저 차단/연결 오류 등
                const err = document.querySelector('[class*=""error""]');
                if (err && err.offsetParent !== null && err.innerText.length > 5) {
                    const txt = err.innerText.trim();
                    if (txt.includes('문제가 발생') || txt.includes('Something went wrong') || txt.includes('error')) {
                        return txt.substring(0, 100);
                    }
                }
                
                return '';
            })()
        ";

        /// <summary>
        /// 오류 상태 복구 시도 (다시 시도 버튼 클릭 등)
        /// </summary>
        public const string RecoverFromErrorScript = @"
            (function() {
                // 스낵바나 에러 영역 내의 버튼(다시 시도, Retry 등) 검색
                const containers = ['m-snackbar', 'snack-bar', '.snackbar', '[role=""alert""]'];
                for (const sel of containers) {
                    const el = document.querySelector(sel);
                    if (el && el.offsetParent !== null) {
                        const btn = el.querySelector('button');
                        if (btn && btn.offsetParent !== null) {
                            btn.click();
                            return 'clicked_recovery_button';
                        }
                    }
                }
                
                // 버튼을 찾지 못했다면 스낵바 자체를 클릭하여 닫기 시도
                const snackbar = document.querySelector('m-snackbar, snack-bar');
                if (snackbar && snackbar.offsetParent !== null) {
                    snackbar.click();
                    return 'clicked_snackbar_to_dismiss';
                }
                
                return 'no_action_taken';
            })()
        ";

        #endregion

        #region 이미지 기능 진단

        /// <summary>
        /// 이미지 기능 사용 가능 여부 진단 (로그인 상태, Pro 모드, 업로드 버튼 등)
        /// </summary>
        public const string DiagnoseImageCapabilityScript = @"
            (function() {
                const result = {
                    available: false,
                    loginRequired: false,
                    proModeAvailable: false,
                    uploadButtonExists: false,
                    errorMessage: ''
                };
                
                try {
                    // 1. 로그인 상태 확인
                    const loginBtn = document.querySelector('button[aria-label*=""로그인""], button[aria-label*=""Sign in""]');
                    if (loginBtn && loginBtn.offsetParent !== null) {
                        result.loginRequired = true;
                        result.errorMessage = '이미지 기능을 사용하려면 로그인이 필요합니다.';
                        return JSON.stringify(result);
                    }
                    
                    // 2. Pro 모드 전환 버튼 (이미지 생성에 필요)
                    const modeBtn = document.querySelector('button.input-area-switch') ||
                                   document.querySelector('button[aria-haspopup=""true""]');
                    result.proModeAvailable = modeBtn && modeBtn.offsetParent !== null;
                    
                    // 3. 업로드 버튼 존재 확인
                    const uploadSelectors = [
                        'button[aria-label*=""파일 업로드 메뉴""]',
                        'button.upload-card-button',
                        'button[aria-label*=""Open file upload""]',
                        'button[aria-label*=""파일 업로드""]'
                    ];
                    
                    for (const sel of uploadSelectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) {
                            result.uploadButtonExists = true;
                            break;
                        }
                    }
                    
                    // 4. 이미지 제한 메시지 확인
                    const bodyText = document.body.innerText;
                    const restrictionPhrases = [
                        '이미지를 생성하려면 로그인',
                        '이미지 업로드를 사용할 수 없',
                        '이 기능은 로그인 후',
                        'Sign in to use image',
                        'Image generation requires',
                        'Login required for image'
                    ];
                    
                    for (const phrase of restrictionPhrases) {
                        if (bodyText.includes(phrase)) {
                            result.loginRequired = true;
                            result.errorMessage = phrase;
                            return JSON.stringify(result);
                        }
                    }
                    
                    // 5. 종합 판단
                    result.available = result.proModeAvailable || result.uploadButtonExists;
                    
                } catch (e) {
                    result.errorMessage = 'Error: ' + e.message;
                }
                
                return JSON.stringify(result);
            })()
        ";

        /// <summary>
        /// 이미지 업로드/생성 관련 오류 메시지 감지
        /// </summary>
        public const string DetectImageErrorScript = @"
            (function() {
                const result = {
                    hasError: false,
                    errorType: '',
                    message: ''
                };
                
                try {
                    // 1. 에러 토스트/스낵바 확인
                    const errorSelectors = [
                        'm-snackbar',
                        'snack-bar',
                        '.snackbar',
                        '[role=""alert""]',
                        '.error-message',
                        '.cdk-overlay-container .error'
                    ];
                    
                    for (const sel of errorSelectors) {
                        const el = document.querySelector(sel);
                        if (el && el.offsetParent !== null && el.innerText.trim().length > 0) {
                            const text = el.innerText.trim();
                            
                            // 이미지 관련 에러 키워드 확인
                            const imageErrorKeywords = [
                                '이미지', 'image', '업로드', 'upload', 
                                '파일', 'file', '사진', 'photo',
                                '용량', 'size', '형식', 'format',
                                '지원되지 않', 'not supported',
                                '실패', 'failed', '오류', 'error'
                            ];
                            
                            const isImageError = imageErrorKeywords.some(kw => 
                                text.toLowerCase().includes(kw.toLowerCase())
                            );
                            
                            if (isImageError) {
                                result.hasError = true;
                                result.errorType = 'upload_error';
                                result.message = text.substring(0, 200);
                                return JSON.stringify(result);
                            }
                        }
                    }
                    
                    // 2. 이미지 업로드 실패 특수 케이스 (업로드 진행 표시 후 사라짐)
                    const uploadIndicator = document.querySelector('.upload-progress, .uploading');
                    const hasUploadFailed = document.querySelector('.upload-failed, .upload-error');
                    
                    if (hasUploadFailed) {
                        result.hasError = true;
                        result.errorType = 'upload_failed';
                        result.message = '이미지 업로드에 실패했습니다.';
                        return JSON.stringify(result);
                    }
                    
                    // 3. 이미지 생성 제한 메시지
                    const limitSelectors = [
                        '[class*=""limit""]',
                        '[class*=""quota""]',
                        '[class*=""restriction""]'
                    ];
                    
                    for (const sel of limitSelectors) {
                        const el = document.querySelector(sel);
                        if (el && el.offsetParent !== null) {
                            const text = el.innerText.trim();
                            if (text.includes('제한') || text.includes('limit') || text.includes('quota')) {
                                result.hasError = true;
                                result.errorType = 'rate_limit';
                                result.message = text.substring(0, 200);
                                return JSON.stringify(result);
                            }
                        }
                    }
                    
                } catch (e) {
                    result.hasError = true;
                    result.errorType = 'script_error';
                    result.message = e.message;
                }
                
                return JSON.stringify(result);
            })()
        ";

        #endregion

        #region 모델 선택

        /// <summary>
        /// 현재 선택된 모델 확인 (flash/pro/unknown)
        /// </summary>
        public const string GetCurrentModelScript = @"
            (function() {
                const modeBtn = document.querySelector('button.input-area-switch') ||
                               document.querySelector('button[aria-haspopup=""true""][aria-label*=""모델""]') ||
                               document.querySelector('button[aria-haspopup=""true""]');
                               
                if (!modeBtn) return 'unknown';
                
                const text = modeBtn.innerText.toLowerCase();
                
                if (text.includes('flash') || text.includes('빠른')) {
                    return 'flash';
                }
                if (text.includes('pro') && !text.includes('flash')) {
                    return 'pro';
                }
                if (text.includes('deep') || text.includes('thinking')) {
                    return 'deep_research';
                }
                
                return 'unknown';
            })();
        ";

        /// <summary>
        /// 모델 전환 (flash/pro) - NanoBananaAutomation.js 로직 이식
        /// </summary>
        public const string SelectModelScript = @"
            (async function(targetModel) {
                // 유틸리티 함수
                const isInteractable = (el) => {
                    if (!el) return false;
                    const style = window.getComputedStyle(el);
                    return el.offsetParent !== null &&
                           style.display !== 'none' &&
                           style.visibility !== 'hidden' &&
                           !el.disabled;
                };
                
                const safeClick = (el) => {
                    if (!el) return false;
                    el.scrollIntoView({ behavior: 'instant', block: 'center' });
                    el.click();
                    ['mousedown', 'mouseup', 'pointerdown', 'pointerup'].forEach(evt => {
                        el.dispatchEvent(new MouseEvent(evt, { bubbles: true, cancelable: true }));
                    });
                    return true;
                };
                
                const delay = (ms) => new Promise(r => setTimeout(r, ms));
                
                // 1. 현재 모드 확인 (이미 선택됨 체크)
                const modeBtn = document.querySelector('button.input-area-switch') ||
                               document.querySelector('button[aria-haspopup=""true""][aria-label*=""모델""]') ||
                               document.querySelector('button[aria-haspopup=""true""]');
                               
                if (!modeBtn || !isInteractable(modeBtn)) {
                    return 'picker_not_found';
                }
                
                const currentText = modeBtn.innerText.toLowerCase();
                if (targetModel === 'flash' && (currentText.includes('flash') || currentText.includes('빠른'))) {
                    return 'already_selected_flash';
                }
                if (targetModel === 'pro' && currentText.includes('pro') && !currentText.includes('flash')) {
                    return 'already_selected_pro';
                }
                
                // 2. 메뉴 열기
                safeClick(modeBtn);
                await delay(600);
                
                // 3. 메뉴 항목 선택 (다중 선택자)
                const menuSelectors = [
                    'button[role=""menuitemradio""]',
                    'button.mat-mdc-menu-item',
                    '.mat-mdc-menu-content button',
                    'button.bard-mode-list-button',
                    '[role=""menuitem""]',
                    'mat-list-item'
                ];
                
                let menuItems = [];
                for (const sel of menuSelectors) {
                    const found = document.querySelectorAll(sel);
                    if (found.length > 0) {
                        menuItems = Array.from(found);
                        break;
                    }
                }
                
                // 4. 대상 모델 항목 찾기
                let targetItem = null;
                for (const item of menuItems) {
                    const itemText = item.innerText.toLowerCase();
                    if (targetModel === 'flash' && (itemText.includes('flash') || itemText.includes('빠른'))) {
                        targetItem = item;
                        break;
                    }
                    if (targetModel === 'pro' && itemText.includes('pro') && !itemText.includes('flash')) {
                        targetItem = item;
                        break;
                    }
                }
                
                if (targetItem && isInteractable(targetItem)) {
                    safeClick(targetItem);
                    await delay(500);
                    return 'switched_to_' + targetModel;
                }
                
                // 메뉴 닫기 (ESC)
                document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                return 'item_not_found_for_' + targetModel;
            })";

        #endregion

        #region Gemini.com 문제 대응

        /// <summary>
        /// Gemini.com 페이지 문제 종합 대응 스크립트
        /// - 오류 다이얼로그 닫기
        /// - 무한 로딩 대응
        /// - 세션 만료 감지
        /// - Rate Limit 대응
        /// </summary>
        public const string GeminiPageRecoveryScript = @"
            (function() {
                const result = {
                    action: 'none',
                    message: '',
                    needsReload: false
                };
                
                try {
                    // 1. 에러 모달/다이얼로그 닫기 시도
                    const dismissSelectors = [
                        'button[aria-label*=""닫기""]',
                        'button[aria-label*=""Close""]',
                        'button[aria-label*=""확인""]',
                        'button[aria-label*=""OK""]',
                        '.cdk-overlay-backdrop',
                        'button.mat-mdc-dialog-close',
                        '[role=""dialog""] button'
                    ];
                    
                    for (const sel of dismissSelectors) {
                        const btn = document.querySelector(sel);
                        if (btn && btn.offsetParent !== null) {
                            btn.click();
                            result.action = 'dismissed_dialog';
                            result.message = '에러 다이얼로그를 닫았습니다.';
                            return JSON.stringify(result);
                        }
                    }
                    
                    // 2. ""다시 시도"" 버튼 클릭
                    const retrySelectors = [
                        'button:contains(""다시 시도"")',
                        'button:contains(""Retry"")',
                        'button[aria-label*=""다시""]',
                        'button[aria-label*=""retry""]'
                    ];
                    
                    const allButtons = document.querySelectorAll('button');
                    for (const btn of allButtons) {
                        const text = btn.innerText.toLowerCase();
                        if ((text.includes('다시') || text.includes('retry')) && 
                            btn.offsetParent !== null && !btn.disabled) {
                            btn.click();
                            result.action = 'clicked_retry';
                            result.message = '다시 시도 버튼을 클릭했습니다.';
                            return JSON.stringify(result);
                        }
                    }
                    
                    // 3. Rate Limit 감지 (429 응답)
                    const bodyText = document.body.innerText;
                    const rateLimitPhrases = [
                        '요청이 너무 많습니다',
                        'Too many requests',
                        '잠시 후 다시',
                        'try again later',
                        '429',
                        'rate limit'
                    ];
                    
                    for (const phrase of rateLimitPhrases) {
                        if (bodyText.toLowerCase().includes(phrase.toLowerCase())) {
                            result.action = 'rate_limited';
                            result.message = 'Rate limit 감지됨. 잠시 후 다시 시도하세요.';
                            return JSON.stringify(result);
                        }
                    }
                    
                    // 4. 세션 만료 감지
                    const sessionPhrases = [
                        '세션이 만료',
                        'session expired',
                        '다시 로그인',
                        'sign in again'
                    ];
                    
                    for (const phrase of sessionPhrases) {
                        if (bodyText.toLowerCase().includes(phrase.toLowerCase())) {
                            result.action = 'session_expired';
                            result.message = '세션이 만료되었습니다. 페이지를 새로고침하세요.';
                            result.needsReload = true;
                            return JSON.stringify(result);
                        }
                    }
                    
                    // 5. 무한 로딩 감지 (입력창이 10초 이상 나타나지 않은 경우에 대한 대응)
                    const loadingIndicators = document.querySelectorAll('.loading, .spinner, [aria-busy=""true""]');
                    const inputReady = document.querySelector('.ql-editor, div[contenteditable=""true""]');
                    
                    if (loadingIndicators.length > 0 && !inputReady) {
                        // 로딩 중이지만 입력창이 없음 - 페이지 새로고침 권장
                        result.action = 'infinite_loading';
                        result.message = '페이지가 로딩 중입니다. 잠시 기다리거나 새로고침하세요.';
                        return JSON.stringify(result);
                    }
                    
                    // 6. 페이지 오류 감지 (빈 페이지, 크래시 등)
                    if (!document.querySelector('body') || document.body.innerHTML.trim().length < 100) {
                        result.action = 'page_error';
                        result.message = '페이지가 제대로 로드되지 않았습니다.';
                        result.needsReload = true;
                        return JSON.stringify(result);
                    }
                    
                } catch (e) {
                    result.action = 'script_error';
                    result.message = e.message;
                }
                
                return JSON.stringify(result);
            })()
        ";

        /// <summary>
        /// 입력창 복구 스크립트 - 입력창이 비정상 상태일 때 복구 시도
        /// </summary>
        public const string RestoreInputScript = @"
            (function() {
                try {
                    const input = document.querySelector('.ql-editor') || 
                                  document.querySelector('div[contenteditable=""true""]');
                    
                    if (!input) return 'input_not_found';
                    
                    // 입력창 포커스 및 상태 복구
                    input.focus();
                    input.setAttribute('contenteditable', 'true');
                    
                    // 비정상 상태 초기화
                    if (input.classList.contains('ql-blank') && input.innerHTML !== '<p><br></p>') {
                        input.innerHTML = '<p><br></p>';
                    }
                    
                    return 'restored';
                } catch (e) {
                    return 'error: ' + e.message;
                }
            })()
        ";

        #endregion

        #region 외부 스크립트 로딩

        /// <summary>
        /// 스크립트 파일 경로 반환
        /// </summary>
        private static string GetScriptPath(string filename)
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Scripts", filename);
        }

        /// <summary>
        /// GeminiCommon.js (공유 유틸리티) 로드
        /// </summary>
        public static string LoadGeminiCommonScript()
        {
            var path = GetScriptPath("GeminiCommon.js");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
        }

        /// <summary>
        /// BrowserModeAutomation.js (브라우저 모드 전용) 로드
        /// </summary>
        public static string LoadBrowserModeScript()
        {
            var path = GetScriptPath("BrowserModeAutomation.js");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
        }

        /// <summary>
        /// NanoBananaAutomation.js (이미지 처리 전용) 로드
        /// </summary>
        public static string LoadNanoBananaScript()
        {
            var path = GetScriptPath("NanoBananaAutomation.js");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
        }

        /// <summary>
        /// 브라우저 모드에 필요한 모든 스크립트 로드 (GeminiCommon + BrowserModeAutomation)
        /// </summary>
        public static string LoadAllBrowserModeScripts()
        {
            var common = LoadGeminiCommonScript();
            var browserMode = LoadBrowserModeScript();
            return $"{common}\n\n{browserMode}";
        }

        /// <summary>
        /// NanoBanana에 필요한 모든 스크립트 로드 (GeminiCommon + NanoBanana)
        /// </summary>
        public static string LoadAllNanoBananaScripts()
        {
            var common = LoadGeminiCommonScript();
            var nanoBanana = LoadNanoBananaScript();
            return $"{common}\n\n{nanoBanana}";
        }

        #endregion

    }
}
