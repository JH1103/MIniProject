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

  const cardInput = document.querySelector('[data-card-number="true"]');
  if (cardInput) {
    const status = document.getElementById('cardNumberStatus');

    const formatCardNumber = (value) => {
      const digits = value.replace(/\D/g, '').slice(0, 16);
      return digits.replace(/(\d{4})(?=\d)/g, '$1 ').trim();
    };

    const luhnCheck = (digits) => {
      let sum = 0;
      let alternate = false;
      for (let i = digits.length - 1; i >= 0; i -= 1) {
        let n = digits.charCodeAt(i) - 48;
        if (alternate) {
          n *= 2;
          if (n > 9) n -= 9;
        }
        sum += n;
        alternate = !alternate;
      }
      return sum % 10 === 0;
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
      if (luhnCheck(digits)) {
        status.textContent = '16-digit card number looks valid.';
        status.className = 'small mt-1 text-success';
      } else {
        status.textContent = '16 digits entered, but the number failed validation.';
        status.className = 'small mt-1 text-danger';
      }
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
