using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Общий источник управления для машины. Игрок и ИИ наследуются от него,
    /// поэтому ArcadeCarController и NitroSystem не знают, кто именно рулит.
    /// </summary>
    public abstract class CarInputSource : MonoBehaviour
    {
        /// <summary>-1 = тормоз/задний ход, +1 = полный газ.</summary>
        public float Throttle { get; protected set; }

        /// <summary>-1 = влево, +1 = вправо.</summary>
        public float Steer { get; protected set; }

        /// <summary>Кнопка заноса удерживается.</summary>
        public bool Drift { get; protected set; }

        private bool _nitroLatch;

        /// <summary>
        /// Нажатие нитро «защёлкивается» до прочтения, чтобы не терять нажатия
        /// между Update (ввод) и FixedUpdate/Update потребителя.
        /// </summary>
        protected void PressNitro() => _nitroLatch = true;

        public bool ConsumeNitroPress()
        {
            bool pressed = _nitroLatch;
            _nitroLatch = false;
            return pressed;
        }

        public virtual void ResetInput()
        {
            Throttle = 0f;
            Steer = 0f;
            Drift = false;
            _nitroLatch = false;
        }
    }
}
