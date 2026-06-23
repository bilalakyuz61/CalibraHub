import { useState, useRef, useEffect } from 'react'
import { X, Camera, RotateCcw, Check, Video, Square, Upload } from 'lucide-react'
import './CameraCapture.css'
import * as api from '../../services/notesService'

function fmt(s) {
  return String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0')
}

export function CameraCapture({ open, onClose, onInsertImage, noteId, onAttachmentAdded }) {
  var [mode, setMode]           = useState('photo')      // photo | video
  var [phase, setPhase]         = useState('preview')    // preview | captured | recording | done
  var [capturedUrl, setCapturedUrl] = useState(null)
  var [videoUrl, setVideoUrl]   = useState(null)
  var [elapsed, setElapsed]     = useState(0)
  var [uploading, setUploading] = useState(false)
  var [error, setError]         = useState(null)
  var [facingMode, setFacingMode] = useState('user')

  var videoRef      = useRef(null)
  var streamRef     = useRef(null)
  var mediaRecRef   = useRef(null)
  var chunksRef     = useRef([])
  var blobRef       = useRef(null)
  var timerRef      = useRef(null)

  useEffect(function () {
    if (open) startStream()
    else stopAll()
    return stopAll
  }, [open, facingMode])

  function stopAll() {
    if (streamRef.current) streamRef.current.getTracks().forEach(function (t) { t.stop() })
    if (timerRef.current) clearInterval(timerRef.current)
    if (videoUrl) URL.revokeObjectURL(videoUrl)
    streamRef.current = null; blobRef.current = null; chunksRef.current = []
    setPhase('preview'); setElapsed(0); setCapturedUrl(null)
    setVideoUrl(null); setError(null)
  }

  async function startStream() {
    setError(null)
    try {
      var stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: facingMode, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: true,
      })
      streamRef.current = stream
      if (videoRef.current) {
        videoRef.current.srcObject = stream
        videoRef.current.play().catch(function () {})
      }
    } catch (err) {
      setError('Kamera erişimi reddedildi: ' + err.message)
    }
  }

  function capturePhoto() {
    var vid = videoRef.current
    if (!vid || !vid.videoWidth) return
    var canvas = document.createElement('canvas')
    canvas.width = vid.videoWidth; canvas.height = vid.videoHeight
    canvas.getContext('2d').drawImage(vid, 0, 0)
    setCapturedUrl(canvas.toDataURL('image/jpeg', 0.92))
    setPhase('captured')
  }

  function retakePhoto() {
    setCapturedUrl(null); setPhase('preview')
  }

  function insertPhoto() {
    if (capturedUrl) { onInsertImage(capturedUrl); onClose() }
  }

  function startVideoRecord() {
    if (!streamRef.current) return
    chunksRef.current = []
    var mime = MediaRecorder.isTypeSupported('video/webm;codecs=vp9,opus')
      ? 'video/webm;codecs=vp9,opus' : 'video/webm'
    var mr = new MediaRecorder(streamRef.current, { mimeType: mime })
    mediaRecRef.current = mr
    mr.ondataavailable = function (e) { if (e.data.size > 0) chunksRef.current.push(e.data) }
    mr.onstop = function () {
      var blob = new Blob(chunksRef.current, { type: mime })
      blobRef.current = blob
      setVideoUrl(URL.createObjectURL(blob))
      setPhase('done')
    }
    mr.start(100)
    setPhase('recording')
    timerRef.current = setInterval(function () { setElapsed(function (s) { return s + 1 }) }, 1000)
  }

  function stopVideoRecord() {
    if (mediaRecRef.current && mediaRecRef.current.state !== 'inactive') mediaRecRef.current.stop()
    clearInterval(timerRef.current)
  }

  function retakeVideo() {
    setPhase('preview')
    setElapsed(0)
    setVideoUrl(null)
    blobRef.current = null
  }

  function uploadVideo() {
    if (!blobRef.current || !noteId) return
    setUploading(true); setError(null)
    var d = new Date()
    var dateStr = d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0')
      + ' ' + String(d.getHours()).padStart(2,'0') + '-' + String(d.getMinutes()).padStart(2,'0')
    var filename = 'Video Kayit ' + dateStr + '.webm'
    var file = new File([blobRef.current], filename, { type: blobRef.current.type })
    api.uploadAttachment(noteId, file)
      .then(function (data) {
        if (data.success) { onAttachmentAdded && onAttachmentAdded(); onClose() }
        else setError(data.message || 'Yükleme başarısız.')
      })
      .catch(function (e) { setError('Yükleme hatası: ' + e.message) })
      .finally(function () { setUploading(false) })
  }

  function flipCamera() {
    setFacingMode(function (f) { return f === 'user' ? 'environment' : 'user' })
  }

  if (!open) return null

  return (
    <div className="cc-backdrop" onClick={function (e) { if (e.target === e.currentTarget) onClose() }}>
      <div className="cc-panel">
        <div className="cc-header">
          <div className="cc-mode-tabs">
            <button className={'cc-tab' + (mode === 'photo' ? ' cc-tab--active' : '')}
              onClick={function () { setMode('photo'); setPhase('preview') }}>
              📷 Fotoğraf
            </button>
            <button className={'cc-tab' + (mode === 'video' ? ' cc-tab--active' : '')}
              onClick={function () { setMode('video'); setPhase('preview') }}>
              🎥 Video
            </button>
          </div>
          <button className="cc-close" onClick={onClose}><X size={18} /></button>
        </div>

        <div className="cc-viewfinder">
          {/* Canlı görüntü — captured/done dışında hep göster */}
          <video
            ref={videoRef} autoPlay playsInline muted
            className="cc-video"
            style={{ display: (phase === 'captured' || phase === 'done') ? 'none' : 'block' }}
          />
          {phase === 'captured' && capturedUrl && (
            <img className="cc-preview-img" src={capturedUrl} alt="çekilen fotoğraf" />
          )}
          {phase === 'done' && videoUrl && (
            <video className="cc-video" controls src={videoUrl} />
          )}
          {phase === 'recording' && (
            <div className="cc-rec-badge">⏺ {fmt(elapsed)}</div>
          )}
          {error && <div className="cc-error">{error}</div>}
        </div>

        <div className="cc-controls">
          {/* FOTOĞRAF — preview */}
          {mode === 'photo' && phase === 'preview' && (<>
            <button className="cc-btn cc-btn--flip" onClick={flipCamera} title="Kamerayı çevir">
              <RotateCcw size={16} />
            </button>
            <button className="cc-btn cc-btn--capture" onClick={capturePhoto}>
              <Camera size={22} />
            </button>
            <div style={{ width: 68 }} />
          </>)}
          {/* FOTOĞRAF — çekildi */}
          {mode === 'photo' && phase === 'captured' && (<>
            <button className="cc-btn cc-btn--retake" onClick={retakePhoto}>
              <RotateCcw size={15} /> Tekrar
            </button>
            <button className="cc-btn cc-btn--insert" onClick={insertPhoto}>
              <Check size={15} /> Notaya Ekle
            </button>
          </>)}
          {/* VİDEO — preview */}
          {mode === 'video' && phase === 'preview' && (<>
            <button className="cc-btn cc-btn--flip" onClick={flipCamera}>
              <RotateCcw size={16} />
            </button>
            <button className="cc-btn cc-btn--capture" onClick={startVideoRecord}>
              <Video size={22} />
            </button>
            <div style={{ width: 68 }} />
          </>)}
          {/* VİDEO — kaydediyor */}
          {mode === 'video' && phase === 'recording' && (
            <button className="cc-btn cc-btn--stop" onClick={stopVideoRecord}>
              <Square size={22} fill="currentColor" />
            </button>
          )}
          {/* VİDEO — bitti */}
          {mode === 'video' && phase === 'done' && (<>
            <button className="cc-btn cc-btn--retake" onClick={retakeVideo}>
              <RotateCcw size={15} /> Tekrar
            </button>
            <button className="cc-btn cc-btn--insert" onClick={uploadVideo} disabled={uploading}>
              <Upload size={15} /> {uploading ? 'Yükleniyor…' : 'Notaya Ekle'}
            </button>
          </>)}
        </div>
      </div>
    </div>
  )
}
