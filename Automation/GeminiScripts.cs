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
        /// 최신 응답 텍스트 추출 (간소화)
        /// </summary>
        public const string GetResponseScript = @"
            (function() {
                const responses = document.querySelectorAll('message-content.model-response-text, .model-response-text');
                if (responses.length === 0) return '';
                return (responses[responses.length - 1].innerText || '').trim();
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

        #region 모델 선택

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

    }
}
