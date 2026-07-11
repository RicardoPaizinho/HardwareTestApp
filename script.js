// Menu Navigation
document.querySelectorAll(".menu-item").forEach((item) => {
  item.addEventListener("click", function (e) {
    e.preventDefault();

    // Remove active class from all menu items
    document
      .querySelectorAll(".menu-item")
      .forEach((i) => i.classList.remove("active"));

    // Add active class to clicked item
    this.classList.add("active");

    // Hide all pages
    document
      .querySelectorAll(".page")
      .forEach((page) => page.classList.add("hidden"));

    // Show selected page
    const pageName = this.getAttribute("data-page");
    document.getElementById(pageName).classList.remove("hidden");
  });
});

// Card click navigation
document.querySelectorAll(".card").forEach((card) => {
  card.addEventListener("click", function () {
    const testType = this.getAttribute("data-test");

    // Update menu
    document
      .querySelectorAll(".menu-item")
      .forEach((i) => i.classList.remove("active"));
    document.querySelector(`[data-page="${testType}"]`).classList.add("active");

    // Show page
    document
      .querySelectorAll(".page")
      .forEach((page) => page.classList.add("hidden"));
    document.getElementById(testType).classList.remove("hidden");
  });
});

// Performance Panel Toggle
const toggleBtn = document.getElementById("togglePerformance");
const rightSidebar = document.querySelector(".right-sidebar");

toggleBtn.addEventListener("click", function () {
  rightSidebar.classList.toggle("collapsed");

  if (rightSidebar.classList.contains("collapsed")) {
    toggleBtn.textContent = "▶";
  } else {
    toggleBtn.textContent = "◀";
  }
});

// Speaker Test
const playSoundBtn = document.getElementById("playSoundBtn");
const testAudio = document.getElementById("testAudio");

playSoundBtn.addEventListener("click", async function () {
  // Generate a simple test tone using Web Audio API
  const audioContext = new (window.AudioContext || window.webkitAudioContext)();
  const oscillator = audioContext.createOscillator();
  const gainNode = audioContext.createGain();

  oscillator.connect(gainNode);
  gainNode.connect(audioContext.destination);

  oscillator.frequency.value = 440; // A4 note
  oscillator.type = "sine";

  gainNode.gain.setValueAtTime(0.3, audioContext.currentTime);
  gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 2);

  oscillator.start(audioContext.currentTime);
  oscillator.stop(audioContext.currentTime + 2);

  playSoundBtn.textContent = "Reproduzindo...";
  playSoundBtn.disabled = true;

  setTimeout(() => {
    playSoundBtn.textContent = "Reproduzir Som";
    playSoundBtn.disabled = false;
  }, 2000);
});

// Microphone Test
const startRecordBtn = document.getElementById("startRecordBtn");
const stopRecordBtn = document.getElementById("stopRecordBtn");
const recordedAudio = document.getElementById("recordedAudio");

let mediaRecorder;
let audioChunks = [];

startRecordBtn.addEventListener("click", async function () {
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    mediaRecorder = new MediaRecorder(stream);

    mediaRecorder.ondataavailable = (event) => {
      audioChunks.push(event.data);
    };

    mediaRecorder.onstop = () => {
      const audioBlob = new Blob(audioChunks, { type: "audio/wav" });
      const audioUrl = URL.createObjectURL(audioBlob);
      recordedAudio.src = audioUrl;
      recordedAudio.classList.remove("hidden");
      audioChunks = [];
    };

    mediaRecorder.start();
    startRecordBtn.classList.add("hidden");
    stopRecordBtn.classList.remove("hidden");
  } catch (error) {
    alert("Erro ao acessar o microfone: " + error.message);
  }
});

stopRecordBtn.addEventListener("click", function () {
  mediaRecorder.stop();
  mediaRecorder.stream.getTracks().forEach((track) => track.stop());
  startRecordBtn.classList.remove("hidden");
  stopRecordBtn.classList.add("hidden");
});

// Camera Test
const startCameraBtn = document.getElementById("startCameraBtn");
const stopCameraBtn = document.getElementById("stopCameraBtn");
const cameraVideo = document.getElementById("cameraVideo");

let cameraStream;

startCameraBtn.addEventListener("click", async function () {
  try {
    cameraStream = await navigator.mediaDevices.getUserMedia({ video: true });
    cameraVideo.srcObject = cameraStream;
    cameraVideo.classList.remove("hidden");
    startCameraBtn.classList.add("hidden");
    stopCameraBtn.classList.remove("hidden");
  } catch (error) {
    alert("Erro ao acessar a câmera: " + error.message);
  }
});

stopCameraBtn.addEventListener("click", function () {
  if (cameraStream) {
    cameraStream.getTracks().forEach((track) => track.stop());
    cameraVideo.srcObject = null;
    cameraVideo.classList.add("hidden");
    startCameraBtn.classList.remove("hidden");
    stopCameraBtn.classList.add("hidden");
  }
});

// Update PC Info from C#
function updatePCInfo(model, serialNumber) {
  document.getElementById("pcModel").textContent = model;
  document.getElementById("serialNumber").textContent = serialNumber;
}

// Update Performance Data from C#
function updatePerformance(cpu, memory, disk) {
  document.getElementById("cpuBar").style.width = cpu + "%";
  document.getElementById("cpuValue").textContent = cpu + "%";

  document.getElementById("memoryBar").style.width = memory + "%";
  document.getElementById("memoryValue").textContent = memory + "%";

  document.getElementById("diskBar").style.width = disk + "%";
  document.getElementById("diskValue").textContent = disk + "%";
}

// Request initial data from C# and handle messages
window.addEventListener("DOMContentLoaded", function () {
  if (window.chrome && window.chrome.webview) {
    // Request initial PC Info
    //window.chrome.webview.postMessage(JSON.stringify({ type: "requestPCInfo" }));

    // Request initial Performance Data and then every 2 seconds
     // window.chrome.webview.postMessage(JSON.stringify({ type: "requestPerformance" }));
      window.chrome.webview.postMessage({ type: "requestPCInfo" });
      window.chrome.webview.postMessage({ type: "requestPerformance" });

    setInterval(() => {
       //window.chrome.webview.postMessage(JSON.stringify({ type: "requestPerformance" }));
        window.chrome.webview.postMessage({ type: "requestPerformance" });
    }, 2000);

    // Handle messages from C#
    window.chrome.webview.addEventListener("message", (event) => {
      const data = JSON.parse(event.data);
      switch (data.type) {
        case "pcInfo":
          updatePCInfo(data.model, data.serialNumber);
          break;
        case "performanceData":
          updatePerformance(data.cpu, data.memory, data.disk);
          break;
        // Add other cases for hardware test results if needed
      }
    });
  }
});

