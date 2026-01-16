using System;
using System.Threading;
using System.Windows.Forms;
using GeminiWebTranslator.Forms;

namespace GeminiWebTranslator
{
    static class Program
    {
        // 고유 Mutex 이름 (GUID 기반으로 다른 앱과 충돌 방지)
        private const string MutexName = "Global\\GeminiWebTranslator_F8A3D2E1-7B4C-4F5E-9A1B-3C8D6E2F0A5B";
        
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Mutex를 사용하여 다중 실행 방지
            using var mutex = new Mutex(true, MutexName, out bool isNewInstance);
            
            if (!isNewInstance)
            {
                // 이미 실행 중인 인스턴스가 있음
                MessageBox.Show(
                    "GeminiWebTranslator가 이미 실행 중입니다.\n기존 창을 확인해주세요.",
                    "알림",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
