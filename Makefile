all: deploy/prod

.PHONY: deploy/prod deploy/dev build/release build/dev sync/prod sync/dev restartsite recyclepool clean

build/release:
	dotnet publish -c Release

build/dev:
	dotnet publish -c Debug

sync/prod:
	rsync --delete -du ./bin/release/net8.0/publish/ //volumes/Websites/FrameVue_AI

sync/dev:
	rsync --delete -du ./bin/debug/net8.0/publish/ //volumes/Websites/Dev_FrameVue_AI

deploy/prod: build/release sync/prod recyclepool

deploy/dev: build/dev sync/dev

recyclepool:
	ssh rjohnson@lifesaversoft@lifesav-web03 -- "\windows\system32\inetsrv\appcmd.exe recycle apppool /apppool.name:FrameVue_AI && exit"

restartsite:
	ssh rjohnson@lifesaversoft@lifesav-web03 -- "\windows\system32\inetsrv\appcmd.exe stop site /site.name:AI.FrameVue.com && \windows\system32\inetsrv\appcmd.exe start site /site.name:AI.FrameVue.com && exit"

clean:
	rm -r ./obj
	rm -r ./bin
