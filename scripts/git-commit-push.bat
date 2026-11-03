@echo off
git branch -M main
git remote add origin https://github.com/SaiVatsal/ControlDesk.git 2>nul
git remote set-url origin https://github.com/SaiVatsal/ControlDesk.git
git commit -m "feat: complete ControlDesk solution with cross-border WebRTC, unattended access, and installer" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
git push -u origin main
