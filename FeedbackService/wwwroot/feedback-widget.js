/**
 * KAN-1: in-app release feedback widget (5-star rating + optional comment).
 *
 * Framework-agnostic, dependency-free. A host page includes this script and calls
 * FeedbackWidget.init({ release, authToken }) once its own "current release" and session
 * token are known.
 *
 * Usage:
 *   <link rel="stylesheet" href="/feedback-widget.css">
 *   <script src="/feedback-widget.js"></script>
 *   <script>
 *     FeedbackWidget.init({
 *       release: "2.4",       // required - the app's current release/build version identifier
 *       authToken: "user-42", // required - forwarded as "Authorization: Bearer <authToken>"
 *       endpoint: "/api/feedback" // optional - defaults to "/api/feedback"
 *     });
 *   </script>
 */
(function (global) {
  'use strict';

  var STORAGE_KEY = 'feedbackWidget.lastHandledRelease';
  var MAX_COMMENT_LENGTH = 500;
  var STAR_VALUES = [1, 2, 3, 4, 5];
  var GENERIC_ERROR_MESSAGE = 'Something went wrong submitting your feedback. Please try again.';
  var OFFLINE_ERROR_MESSAGE = "You're offline or the server can't be reached. Please try again.";
  var RATING_REQUIRED_MESSAGE = 'Please pick a star rating before submitting.';
  var CONFIRMATION_AUTO_CLOSE_MS = 6000;

  // localStorage can throw (private browsing, storage disabled by policy, quota). A widget
  // whose only job is "collect feedback" must never break the host page because of it - so
  // every read/write is wrapped and failures are treated as "nothing recorded".
  function readLastHandledRelease() {
    try {
      return global.localStorage.getItem(STORAGE_KEY);
    } catch (err) {
      return null;
    }
  }

  function writeLastHandledRelease(release) {
    try {
      global.localStorage.setItem(STORAGE_KEY, release);
    } catch (err) {
      // Swallowed deliberately - see readLastHandledRelease.
    }
  }

  function el(tag, attrs, children) {
    var node = global.document.createElement(tag);
    attrs = attrs || {};
    Object.keys(attrs).forEach(function (key) {
      if (key === 'className') {
        node.className = attrs[key];
      } else if (key === 'text') {
        node.textContent = attrs[key];
      } else {
        node.setAttribute(key, attrs[key]);
      }
    });
    (children || []).forEach(function (child) {
      node.appendChild(child);
    });
    return node;
  }

  function buildWidget(state) {
    var root = el('div', {
      className: 'feedback-widget',
      role: 'dialog',
      'aria-label': 'Release feedback'
    });

    var closeButton = el('button', {
      type: 'button',
      className: 'feedback-widget__close',
      'aria-label': 'Dismiss feedback widget'
    }, [global.document.createTextNode('×')]);
    closeButton.addEventListener('click', function () {
      writeLastHandledRelease(state.release); // AC-8: dismissal is recorded, same as answering
      state.remove();
    });

    var body = el('div', { className: 'feedback-widget__body' });

    var prompt = el('p', { className: 'feedback-widget__prompt', text: 'How would you rate this release?' });

    var starGroup = el('div', {
      className: 'feedback-widget__stars',
      role: 'radiogroup',
      'aria-label': 'Star rating'
    });

    var starButtons = STAR_VALUES.map(function (value) {
      var star = el('button', {
        type: 'button',
        className: 'feedback-widget__star',
        role: 'radio',
        'aria-checked': 'false',
        'aria-label': value + (value === 1 ? ' star' : ' stars')
      }, [global.document.createTextNode('★')]);
      star.addEventListener('click', function () {
        state.rating = value;
        starButtons.forEach(function (button, index) {
          var filled = index < value;
          button.classList.toggle('feedback-widget__star--filled', filled);
          button.setAttribute('aria-checked', String(index + 1 === value));
        });
        hideError();
      });
      starGroup.appendChild(star);
      return star;
    });

    var comment = el('textarea', {
      className: 'feedback-widget__comment',
      maxlength: String(MAX_COMMENT_LENGTH),
      placeholder: "Anything you'd like to add? (optional)",
      'aria-label': 'Comment (optional)'
    });

    var counter = el('div', { className: 'feedback-widget__counter' });

    function updateCounter() {
      // Defensive truncation in addition to the maxlength attribute (AC-9): maxlength alone
      // does not protect against a value assigned programmatically to this element.
      if (comment.value.length > MAX_COMMENT_LENGTH) {
        comment.value = comment.value.slice(0, MAX_COMMENT_LENGTH);
      }
      counter.textContent = (MAX_COMMENT_LENGTH - comment.value.length) + ' characters left';
    }
    comment.addEventListener('input', updateCounter);
    updateCounter();

    var errorBox = el('div', { className: 'feedback-widget__error', role: 'alert' });
    errorBox.hidden = true;

    function showError(message) {
      errorBox.textContent = message;
      errorBox.hidden = false;
    }
    function hideError() {
      errorBox.hidden = true;
    }

    var submitButton = el('button', {
      type: 'button',
      className: 'feedback-widget__submit',
      text: 'Submit'
    });

    function setSubmitting(isSubmitting) {
      submitButton.disabled = isSubmitting;
      submitButton.textContent = isSubmitting ? 'Submitting…' : 'Submit';
    }

    function showConfirmation(confirmedRelease) {
      body.innerHTML = '';
      var thanks = el('p', {
        className: 'feedback-widget__thanks',
        text: 'Thanks, logged for ' + confirmedRelease + '.'
      });
      body.appendChild(thanks);
      global.setTimeout(state.remove, CONFIRMATION_AUTO_CLOSE_MS);
    }

    submitButton.addEventListener('click', function () {
      if (!state.rating) {
        showError(RATING_REQUIRED_MESSAGE); // AC-7: blocked, visible message, not a silent failure
        return;
      }
      hideError();
      setSubmitting(true);

      var payload = { release: state.release, rating: state.rating, comment: comment.value };
      var headers = { 'Content-Type': 'application/json' };
      if (state.authToken) {
        headers.Authorization = 'Bearer ' + state.authToken;
      }

      global.fetch(state.endpoint, {
        method: 'POST',
        headers: headers,
        body: JSON.stringify(payload)
      }).then(function (response) {
        if (response.ok) {
          return response.json().then(function (created) {
            var confirmedRelease = created && created.release ? created.release : state.release;
            writeLastHandledRelease(state.release); // AC-3: only on success
            showConfirmation(confirmedRelease); // AC-11
          });
        }
        return response.json().catch(function () {
          return null;
        }).then(function (errorBody) {
          setSubmitting(false);
          showError(errorBody && errorBody.message ? errorBody.message : GENERIC_ERROR_MESSAGE);
          // AC-10: rating/comment are left exactly as the user entered them, and the release
          // is deliberately NOT recorded - writeLastHandledRelease is not called on this path.
        });
      }).catch(function () {
        setSubmitting(false);
        showError(OFFLINE_ERROR_MESSAGE); // AC-10: network/transport failure
      });
    });

    body.appendChild(prompt);
    body.appendChild(starGroup);
    body.appendChild(comment);
    body.appendChild(counter);
    body.appendChild(errorBox);
    body.appendChild(submitButton);

    root.appendChild(closeButton);
    root.appendChild(body);
    return root;
  }

  /**
   * Mounts the widget into the DOM unless this device has already answered or dismissed the
   * current release (AC-1, AC-2, AC-3). Returns the mounted element, or null if it was
   * suppressed. Safe to call on every page load - the caller does not need to check state itself.
   */
  function init(options) {
    options = options || {};
    var release = options.release;
    if (!release || typeof release !== 'string') {
      throw new Error('FeedbackWidget.init requires a non-empty string "release" option.');
    }

    if (readLastHandledRelease() === release) {
      return null; // AC-3: already handled for this exact release, on this device
    }

    var container = options.container || global.document.body;
    var state = {
      release: release,
      endpoint: options.endpoint || '/api/feedback',
      authToken: options.authToken,
      rating: 0
    };

    var node = buildWidget(state);
    state.remove = function () {
      if (node.parentNode) {
        node.parentNode.removeChild(node);
      }
    };

    container.appendChild(node);
    return node;
  }

  global.FeedbackWidget = { init: init };
})(window);
