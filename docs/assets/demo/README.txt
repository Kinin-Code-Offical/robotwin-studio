Hero demo slot for the landing pages (index.html and tr/index.html).

Put a short muted looping clip here, for example robotwin-demo.mp4
(and optionally robotwin-demo.webm), then set the path in docs/_config.yml:

  demo:
    mp4: /assets/demo/robotwin-demo.mp4
    webm: /assets/demo/robotwin-demo.webm
    gif: ""
    poster: /images/world-ps5-car.png

Recommended: 10-20 s, 1600x960 or 1280x768 (5:3, same as the screenshots),
H.264 MP4 without audio, a few MB at most. A GIF works too (set gif: instead),
but MP4/WebM is much smaller. While mp4/webm/gif are empty the poster
screenshot is shown. Visitors with "reduce motion" get the video paused.
