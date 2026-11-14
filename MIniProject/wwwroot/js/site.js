(() => {
  const flashes = document.querySelectorAll('.flash');
  flashes.forEach((el) => {
    setTimeout(() => {
      el.style.transition = 'opacity .4s ease, transform .4s ease';
      el.style.opacity = '0';
      el.style.transform = 'translateY(-6px)';
    }, 4200);
  });

  const tiles = document.querySelectorAll('.facility-tile');
  tiles.forEach((tile, index) => {
    tile.style.animationDelay = `${index * 70}ms`;
  });

  document.querySelectorAll('[data-flip-card]').forEach((card) => {
    const toggle = () => card.classList.toggle('is-flipped');
    card.addEventListener('click', toggle);
    card.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        toggle();
      }
    });
  });

  document.querySelectorAll('[data-password-toggle]').forEach((button) => {
    button.addEventListener('click', () => {
      const field = button.closest('.password-field');
      const input = field?.querySelector('input');
      if (!input) return;

      const showing = input.type === 'text';
      input.type = showing ? 'password' : 'text';
      button.setAttribute('aria-pressed', showing ? 'false' : 'true');
      button.setAttribute('aria-label', showing ? 'Show password' : 'Hide password');
      button.setAttribute('title', showing ? 'Show password' : 'Hide password');

      const eye = button.querySelector('.icon-eye');
      const eyeOff = button.querySelector('.icon-eye-off');
      if (eye && eyeOff) {
        eye.hidden = !showing;
        eyeOff.hidden = showing;
      }
    });
  });

  const cardInput = document.querySelector('[data-card-number="true"]');
  if (cardInput) {
    const status = document.getElementById('cardNumberStatus');

    const formatCardNumber = (value) => {
      const digits = value.replace(/\D/g, '').slice(0, 16);
      return digits.replace(/(\d{4})(?=\d)/g, '$1 ').trim();
    };

    const updateStatus = () => {
      if (!status) return;
      const digits = cardInput.value.replace(/\D/g, '');
      if (digits.length === 0) {
        status.textContent = '';
        status.className = 'small mt-1 muted';
        return;
      }
      if (digits.length < 16) {
        status.textContent = `${digits.length} / 16 digits`;
        status.className = 'small mt-1 muted';
        return;
      }
      status.textContent = '16-digit card number ready.';
      status.className = 'small mt-1 text-success';
    };

    cardInput.addEventListener('input', () => {
      const start = cardInput.selectionStart || 0;
      const before = cardInput.value.length;
      cardInput.value = formatCardNumber(cardInput.value);
      const after = cardInput.value.length;
      const next = Math.max(0, start + (after - before));
      cardInput.setSelectionRange(next, next);
      updateStatus();
    });

    cardInput.addEventListener('blur', updateStatus);
    cardInput.value = formatCardNumber(cardInput.value);
    updateStatus();
  }
})();
