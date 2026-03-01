all: deploy/prod

.PHONY: deploy/prod deploy/dev build/release build/dev sync/prod sync/dev restartsite recyclepool clean

build/release:
	dotnet publish -c Release

build/dev:
	dotnet publish -c Debug

sync/prod:
	rsync --delete -ru --exclude='appsettings.Production.json' --exclude='logs/' --exclude='frameVue.db*' ./bin/release/net8.0/publish/ //volumes/Websites/FrameVue_AI

sync/dev:
	rsync --delete -ru --exclude='appsettings.Production.json' --exclude='logs/' --exclude='frameVue.db*' ./bin/debug/net8.0/publish/ //volumes/Websites/Dev_FrameVue_AI

deploy/prod: build/release sync/prod recyclepool

deploy/dev: build/dev sync/dev

recyclepool:
	touch //volumes/Websites/FrameVue_AI/web.config

clean:
	rm -r ./obj
	rm -r ./bin
